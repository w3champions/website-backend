#nullable enable
using System;
using System.Threading.Tasks;

namespace W3ChampionsStatisticService.PlayerMatchTelemetry;

public interface IPlayerMatchTelemetryRepository
{
    Task UpsertPlayerEntryAsync(
        long gameId,
        DateTime matchWallStart,
        int bucketMs,
        PlayerMatchTelemetryEntry entry,
        TimeSpan ttl);

    Task<PlayerMatchTelemetry?> GetByGameIdAsync(long gameId);

    Task EnsureIndexesAsync();
}
