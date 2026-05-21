using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.PlayerMatchTelemetry;

// Spec: docs/superpowers/specs/2026-05-21-flo-action-latency-design.md §4.8.
[ApiController]
[Route("api/player-match-telemetry")]
[Trace]
public class PlayerMatchTelemetryController(IPlayerMatchTelemetryRepository repo) : ControllerBase
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(90);

    private readonly IPlayerMatchTelemetryRepository _repo = repo;

    /// <summary>
    /// Submit a player's match telemetry — called by the launcher per player after match end.
    /// Authenticated users only (any player, not admin-only).
    /// </summary>
    [HttpPost]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> Submit(
        [FromBody] PlayerMatchTelemetrySubmissionDto submission,
        string actingPlayer)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var ts = submission.ActionLatencyTimeseries;

        var entry = new PlayerMatchTelemetryEntry
        {
            BattleTag = actingPlayer,
            FloPlayerId = null,
            ConnectionType = submission.ConnectionType.ToUpperInvariant(),
            ServerNodeId = null,
            ServerNodeName = null,
            GameLengthMs = submission.GameLengthMs,
            Crashed = submission.Crashed,
            Disconnects = new DisconnectStats
            {
                Count = submission.Disconnects.Count,
                TotalDurationMs = submission.Disconnects.TotalDurationMs,
                MeanDurationMs = submission.Disconnects.MeanDurationMs,
            },
            ActionLatencyAggregate = new ActionLatencyAggregate
            {
                SampleCount = submission.ActionLatencyAggregate.SampleCount,
                P10Ms = submission.ActionLatencyAggregate.P10Ms,
                P50Ms = submission.ActionLatencyAggregate.P50Ms,
                P99Ms = submission.ActionLatencyAggregate.P99Ms,
                P999Ms = submission.ActionLatencyAggregate.P999Ms,
                MeanMs = submission.ActionLatencyAggregate.MeanMs,
                StddevMs = submission.ActionLatencyAggregate.StddevMs,
            },
            BucketCount = ts.MeansMs.Length,
            GameTimeOffsetsMs = new BsonBinaryData(EncodeU32Le(ts.GameTimeOffsetsMs)),
            MeansMs = new BsonBinaryData(EncodeU16Le(ts.MeansMs)),
            SampleCounts = new BsonBinaryData(ts.SampleCounts),
            DroppedUnmatchedCount = submission.DroppedUnmatchedCount,
            SubmittedAt = DateTime.UtcNow,
        };

        await _repo.UpsertPlayerEntryAsync(
            submission.GameId,
            submission.MatchWallStart,
            submission.BucketMs,
            entry,
            DefaultTtl);

        return Ok();
    }

    /// <summary>Fetch a telemetry document by game id. Returns 404 if not present.</summary>
    /// <remarks>
    /// The raw domain model contains <see cref="BsonBinaryData"/> fields that System.Text.Json
    /// cannot serialize (would crash with HTTP 500). We project to a response DTO that emits
    /// MongoDB Extended JSON v2 BinData envelopes the website's IBinData decoder understands.
    /// </remarks>
    [HttpGet("by-game/{gameId:long}")]
    public async Task<IActionResult> GetByGame(long gameId)
    {
        var doc = await _repo.GetByGameIdAsync(gameId);
        if (doc is null) return NotFound();
        return Ok(PlayerMatchTelemetryMapper.ToResponseDto(doc));
    }

    /// <summary>
    /// Encodes a <see cref="uint"/>[] as little-endian bytes via <see cref="Buffer.BlockCopy"/>.
    /// </summary>
    /// <remarks>
    /// On x86_64 and arm64 Linux (the deployment platforms) this produces little-endian output
    /// directly. If the service is ever ported to a big-endian host, swap to
    /// <c>System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian</c> per element.
    /// </remarks>
    private static byte[] EncodeU32Le(uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>
    /// Encodes a <see cref="ushort"/>[] as little-endian bytes via <see cref="Buffer.BlockCopy"/>.
    /// </summary>
    /// <remarks>
    /// On x86_64 and arm64 Linux (the deployment platforms) this produces little-endian output
    /// directly. If the service is ever ported to a big-endian host, swap to
    /// <c>System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian</c> per element.
    /// </remarks>
    private static byte[] EncodeU16Le(ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
