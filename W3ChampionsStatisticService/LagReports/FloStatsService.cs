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
    Task<List<ServerSidePingData>> FetchGamePingData(int floGameId);
    Task FetchAndStoreIfNeeded(int floGameId, LagReportRepository repo);

    /// <summary>flo player id → name (the battletag) for a game; null if unavailable.</summary>
    Task<Dictionary<int, string>> FetchGamePlayers(int floGameId);
}

[Trace]
public class FloStatsService : IFloStatsService
{
    private static readonly string WsEndpoint =
        Environment.GetEnvironmentVariable("FLO_STATS_WS_URL") ?? "wss://stats.w3flo.com/ws";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // Coalesce concurrent fetches for the same game — avoids duplicate WebSocket connections.
    // Entries are removed on completion, so size is bounded by concurrent game endings.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Task<List<ServerSidePingData>>>
        _inflightFetches = new();

    /// <summary>
    /// Fetch accumulated ping data for a game from flo-stats via WebSocket subscription.
    /// Returns null if the game is not found (evicted from LRU) or on any error; an empty list
    /// means the snapshot was read but held no ping samples.
    /// </summary>
    public async Task<List<ServerSidePingData>> FetchGamePingData(int floGameId) =>
        await FetchSnapshot(floGameId) is { } snapshot ? ParsePingData(snapshot) : null;

    public async Task<Dictionary<int, string>> FetchGamePlayers(int floGameId) =>
        await FetchSnapshot(floGameId) is { } snapshot ? ParsePlayers(snapshot) : null;

    // The game's GameSnapshotWithStats payload, or null if flo-stats has no such game or fails.
    private async Task<JsonElement?> FetchSnapshot(int floGameId)
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
                return null;
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
                                game { players { id name } }
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
                return null;
            }

            if (msgType != "next")
            {
                Log.Warning("FloStatsService: unexpected message type {Type} for game {GameId}", msgType, floGameId);
                return null;
            }

            // 5. Parse the snapshot
            var payload = msg.Value.GetProperty("payload").GetProperty("data").GetProperty("gameUpdateEvents");
            var typeName = payload.GetProperty("__typename").GetString();

            if (typeName != "GameSnapshotWithStats")
            {
                Log.Information("FloStatsService: first event for game {GameId} was {Type}, not snapshot", floGameId, typeName);
                return null;
            }

            return payload;
        }
        catch (OperationCanceledException)
        {
            Log.Warning("FloStatsService: timeout fetching ping data for game {GameId}", floGameId);
            return null;
        }
        catch (WebSocketException ex)
        {
            Log.Warning(ex, "FloStatsService: WebSocket error for game {GameId}", floGameId);
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "FloStatsService: failed to fetch ping data for game {GameId}", floGameId);
            return null;
        }
    }

    /// <summary>
    /// Fetch ping data and store it on the lag report if not already populated.
    /// Concurrent calls for the same game share a single WebSocket fetch and a single write.
    /// </summary>
    public async Task FetchAndStoreIfNeeded(int floGameId, LagReportRepository repo)
    {
        var report = await repo.GetByFloGameId(floGameId);
        if (report == null || report.ServerSidePing != null)
        {
            return;
        }

        // Coalesce: all concurrent callers for the same game share one fetch+store task
        var task = _inflightFetches.GetOrAdd(floGameId, id => FetchAndStore(id, repo));
        try
        {
            await task;
        }
        finally
        {
            _inflightFetches.TryRemove(floGameId, out _);
        }
    }

    private async Task<List<ServerSidePingData>> FetchAndStore(int floGameId, LagReportRepository repo)
    {
        var pingData = await FetchGamePingData(floGameId);
        if (pingData == null)
        {
            // Leave ServerSidePing null so a later trigger for this game retries; persisting
            // [] here would block retries forever via the null check in FetchAndStoreIfNeeded.
            return null;
        }

        var report = await repo.GetByFloGameId(floGameId);
        if (report != null && report.ServerSidePing == null)
        {
            await repo.UpdateServerSidePing(report.Id, pingData);
        }

        return pingData;
    }

    internal static Dictionary<int, string> ParsePlayers(JsonElement payload)
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
        return playerNames;
    }

    internal static List<ServerSidePingData> ParsePingData(JsonElement payload)
    {
        var playerNames = ParsePlayers(payload);

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
                        Min = RoundedMs(ReadNumber(d, "min")),
                        Max = RoundedMs(ReadNumber(d, "max")),
                        Avg = ReadNumber(d, "avg"),
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

    // flo-stats encodes avg as a decimal (and may encode min/max as e.g. 87.0), so GetInt32 throws.
    private static double? ReadNumber(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) ? n : null;

    private static int? RoundedMs(double? ms) =>
        ms.HasValue ? (int)Math.Round(ms.Value, MidpointRounding.AwayFromZero) : null;

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
