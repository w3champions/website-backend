using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.LagReports;

/// <summary>
/// Fetches server-side ping data from the flo-stats-service GraphQL API via WebSocket.
/// The stats service uses subscriptions only (no HTTP queries for game stats).
/// We connect, get the initial GameSnapshotWithStats, then disconnect.
/// Data is LRU-cached — may be unavailable for older games.
/// </summary>
public interface IFloStatsService
{
    Task<FloGameSnapshotResult> FetchGameSnapshot(int floGameId);
    Task FetchAndStoreIfNeeded(int floGameId, LagReportRepository repo, FloGameLeaveRepository leaveRepo, EFloLeaveCaptureTrigger trigger);
}

/// <summary>
/// One flo-stats snapshot read. <see cref="Available"/> is false when the game was
/// not in flo's LRU or the fetch failed; both payload lists are then empty. The
/// distinction matters downstream - see FloGameLeaveReport.SnapshotAvailable.
/// </summary>
public class FloGameSnapshotResult
{
    public bool Available { get; set; }
    public List<ServerSidePingData> Ping { get; set; } = [];
    public List<FloPlayerLeave> PlayerLeaves { get; set; } = [];
}

[Trace]
public class FloStatsService : IFloStatsService
{
    private static readonly string WsEndpoint =
        Environment.GetEnvironmentVariable("FLO_STATS_WS_URL") ?? "wss://stats.w3flo.com/ws";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // Coalesce concurrent fetches for the same game — avoids duplicate WebSocket connections.
    // Entries are removed on completion, so size is bounded by concurrent game endings.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Task<FloGameSnapshotResult>>
        _inflightFetches = new();

    /// <summary>
    /// Fetch the accumulated snapshot for a game from flo-stats via WebSocket
    /// subscription. Returns a result with Available = false if the game is not found
    /// (evicted from flo's LRU) or on any error - never null, so callers can record
    /// "we asked and got nothing" as a fact rather than losing it.
    /// </summary>
    public async Task<FloGameSnapshotResult> FetchGameSnapshot(int floGameId)
    {
        try
        {
            using var cts = new CancellationTokenSource(Timeout);
            using var ws = new ClientWebSocket();
            ws.Options.AddSubProtocol("graphql-transport-ws");

            await ws.ConnectAsync(new Uri(WsEndpoint), cts.Token);

            // 1. connection_init
            await SendJson(ws, new { type = "connection_init" }, cts.Token);

            // 2. Wait for connection_ack
            var ack = await ReceiveJson(ws, cts.Token);
            if (ack?.GetProperty("type").GetString() != "connection_ack")
            {
                Log.Warning("FloStatsService: expected connection_ack, got {Type}", ack?.GetProperty("type").GetString());
                return new FloGameSnapshotResult();
            }

            // 3. Subscribe to GameUpdateSub
            await SendJson(ws, new
            {
                id = "1",
                type = "subscribe",
                payload = new
                {
                    query = @"subscription GameUpdateSub($id: Int!) {
                        gameUpdateEvents(id: $id) {
                            __typename
                            ... on GameSnapshotWithStats {
                                stats { ping { time data { playerId min max avg } } }
                                game { players { id name leftAt leaveReason } }
                            }
                        }
                    }",
                    variables = new { id = floGameId },
                }
            }, cts.Token);

            // 4. Read the first "next" message (initial snapshot with accumulated stats)
            //    The server may also send "error" if game not found.
            var msg = await ReceiveJson(ws, cts.Token);
            var msgType = msg?.GetProperty("type").GetString();

            if (msgType == "error" || msgType == "complete")
            {
                Log.Information("FloStatsService: game {GameId} not found in flo-stats (type={Type})", floGameId, msgType);
                return new FloGameSnapshotResult();
            }

            if (msgType != "next")
            {
                Log.Warning("FloStatsService: unexpected message type {Type} for game {GameId}", msgType, floGameId);
                return new FloGameSnapshotResult();
            }

            // 5. Parse the snapshot
            var payload = msg.Value.GetProperty("payload").GetProperty("data").GetProperty("gameUpdateEvents");
            var typeName = payload.GetProperty("__typename").GetString();

            if (typeName != "GameSnapshotWithStats")
            {
                Log.Information("FloStatsService: first event for game {GameId} was {Type}, not snapshot", floGameId, typeName);
                return new FloGameSnapshotResult();
            }

            return new FloGameSnapshotResult
            {
                Available = true,
                Ping = ParsePingData(payload),
                PlayerLeaves = ParsePlayerLeaves(payload),
            };
        }
        catch (OperationCanceledException)
        {
            Log.Warning("FloStatsService: timeout fetching ping data for game {GameId}", floGameId);
            return new FloGameSnapshotResult();
        }
        catch (WebSocketException ex)
        {
            Log.Warning(ex, "FloStatsService: WebSocket error for game {GameId}", floGameId);
            return new FloGameSnapshotResult();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "FloStatsService: failed to fetch ping data for game {GameId}", floGameId);
            return new FloGameSnapshotResult();
        }
    }

    /// <summary>
    /// Fetch the flo-stats snapshot once and store both halves: ping data onto the lag
    /// report (only when one exists and is unpopulated), and leave reasons into
    /// FloGameLeaveReport.
    ///
    /// The leave half is written UNCONDITIONALLY. The old ping-only logic bailed when
    /// no lag report existed, which is precisely the case for a cancelled or abandoned
    /// game - so the games most worth explaining were the ones guaranteed to record
    /// nothing. Concurrent callers for the same game share one WebSocket fetch.
    /// </summary>
    public async Task FetchAndStoreIfNeeded(int floGameId, LagReportRepository repo, FloGameLeaveRepository leaveRepo, EFloLeaveCaptureTrigger trigger)
    {
        var report = await repo.GetByFloGameId(floGameId);
        var existingLeaves = await leaveRepo.GetByFloGameId(floGameId);

        var needsPing = report != null && report.ServerSidePing == null;
        var needsLeaves = existingLeaves == null || !existingLeaves.SnapshotAvailable;
        if (!needsPing && !needsLeaves)
        {
            return;
        }

        // Coalesce: all concurrent callers for the same game share one fetch
        var task = _inflightFetches.GetOrAdd(floGameId, FetchGameSnapshot);
        FloGameSnapshotResult snapshot;
        try
        {
            snapshot = await task;
        }
        finally
        {
            _inflightFetches.TryRemove(floGameId, out _);
        }

        await leaveRepo.Upsert(floGameId, trigger, snapshot.Available, snapshot.PlayerLeaves);

        // Re-read: another caller may have populated the ping data while we were fetching.
        report = await repo.GetByFloGameId(floGameId);
        if (report != null && report.ServerSidePing == null)
        {
            await repo.UpdateServerSidePing(report.Id, snapshot.Ping);
        }
    }

    internal static List<ServerSidePingData> ParsePingData(JsonElement payload)
    {
        var playerNames = new Dictionary<int, string>();
        if (payload.TryGetProperty("game", out var game) &&
            game.TryGetProperty("players", out var players))
        {
            foreach (var p in players.EnumerateArray())
            {
                var id = p.GetProperty("id").GetInt32();
                var name = p.GetProperty("name").GetString();
                playerNames[id] = name;
            }
        }

        var byPlayer = new Dictionary<int, List<ServerPingSample>>();

        if (payload.TryGetProperty("stats", out var stats) &&
            stats.TryGetProperty("ping", out var pingArray))
        {
            foreach (var entry in pingArray.EnumerateArray())
            {
                var time = entry.GetProperty("time").GetDouble();
                if (!entry.TryGetProperty("data", out var dataArray)) continue;

                foreach (var d in dataArray.EnumerateArray())
                {
                    var playerId = d.GetProperty("playerId").GetInt32();
                    if (!byPlayer.ContainsKey(playerId))
                    {
                        byPlayer[playerId] = [];
                    }
                    byPlayer[playerId].Add(new ServerPingSample
                    {
                        Time = time,
                        Min = ReadRoundedInt(d, "min"),
                        Max = ReadRoundedInt(d, "max"),
                        Avg = ReadRoundedInt(d, "avg"),
                    });
                }
            }
        }

        return byPlayer.Select(kv => new ServerSidePingData
        {
            PlayerId = kv.Key,
            PlayerName = playerNames.GetValueOrDefault(kv.Key, $"Player {kv.Key}"),
            Samples = kv.Value,
        }).ToList();
    }

    /// <summary>
    /// Reads a numeric JSON property as an int, rounding a fractional value rather than
    /// rejecting it. flo's Ping.avg is an f32 (crates/observer/src/record.rs) and so
    /// arrives as a GraphQL Float, serialising as "12.0" even when integral -
    /// JsonElement.GetInt32() throws FormatException on that. Because the throw escaped
    /// ParsePingData, it aborted the whole snapshot parse and the caller then persisted
    /// an empty ping array, which the "already populated" guard treated as done and
    /// never retried.
    /// </summary>
    private static int? ReadRoundedInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number)
            ? (int)Math.Round(number)
            : null;

    /// <summary>
    /// Reads per-player leave reasons out of the snapshot's game.players array.
    ///
    /// Emits EVERY player, including those with no leave record: a null reason for a
    /// player who was in the game is itself the signal, since flo records no PlayerLeft
    /// at all when a started game's stream simply closes.
    /// </summary>
    internal static List<FloPlayerLeave> ParsePlayerLeaves(JsonElement payload)
    {
        var leaves = new List<FloPlayerLeave>();

        if (!payload.TryGetProperty("game", out var game) ||
            !game.TryGetProperty("players", out var players) ||
            players.ValueKind != JsonValueKind.Array)
        {
            return leaves;
        }

        foreach (var p in players.EnumerateArray())
        {
            if (!p.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.Number ||
                !idElement.TryGetInt32(out var playerId))
            {
                continue;
            }

            var raw = p.TryGetProperty("leaveReason", out var reason) && reason.ValueKind == JsonValueKind.String
                ? reason.GetString()
                : null;

            leaves.Add(new FloPlayerLeave
            {
                PlayerId = playerId,
                PlayerName = p.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                    ? name.GetString()
                    : null,
                LeaveReasonRaw = raw,
                LeaveReason = MapLeaveReason(raw),
                LeftAtMs = p.TryGetProperty("leftAt", out var leftAt) &&
                           leftAt.ValueKind == JsonValueKind.Number &&
                           leftAt.TryGetInt64(out var ms)
                    ? ms
                    : null,
            });
        }

        return leaves;
    }

    /// <summary>
    /// Maps flo's SCREAMING_SNAKE_CASE PlayerLeaveReason wire strings. Returns null both
    /// for an absent reason and for a variant added to flo after this switch was written;
    /// FloPlayerLeave.LeaveReasonRaw preserves the original either way, so an unmapped
    /// variant is visible in the data instead of silently becoming "no record".
    /// </summary>
    internal static EFloLeaveReason? MapLeaveReason(string wire) => wire switch
    {
        "LEAVE_DISCONNECT" => EFloLeaveReason.LeaveDisconnect,
        "LEAVE_LOST" => EFloLeaveReason.LeaveLost,
        "LEAVE_LOST_BUILDINGS" => EFloLeaveReason.LeaveLostBuildings,
        "LEAVE_WON" => EFloLeaveReason.LeaveWon,
        "LEAVE_DRAW" => EFloLeaveReason.LeaveDraw,
        "LEAVE_OBSERVER" => EFloLeaveReason.LeaveObserver,
        "LEAVE_UNKNOWN" => EFloLeaveReason.LeaveUnknown,
        _ => null,
    };

    private static async Task SendJson<T>(ClientWebSocket ws, T obj, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(obj);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private static async Task<JsonElement?> ReceiveJson(ClientWebSocket ws, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(buffer, ct);
                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            ms.Position = 0;
            var doc = await JsonDocument.ParseAsync(ms, cancellationToken: ct);
            return doc.RootElement.Clone();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
