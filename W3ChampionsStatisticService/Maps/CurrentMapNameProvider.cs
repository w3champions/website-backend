using System;
using System.Threading.Tasks;
using Serilog;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Cache;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// All matchmaking map names by id, cached for the duration configured for
/// <see cref="CacheOptionsFor{T}"/> of <see cref="CurrentMapNames"/>, so a rename shows once the entry expires.
/// A failed fetch caches <see cref="CurrentMapNames.Empty"/> for <see cref="FailureCacheDuration"/>: stored names
/// are shown, and requests don't each wait for matchmaking's timeout while it is unreachable.
/// </summary>
[Trace]
public class CurrentMapNameProvider(
    MatchmakingServiceClient matchmakingServiceClient,
    ICachedDataProvider<CurrentMapNames> cachedDataProvider) : ICurrentMapNameProvider
{
    internal static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(1);

    private readonly MatchmakingServiceClient _matchmakingServiceClient = matchmakingServiceClient;
    private readonly ICachedDataProvider<CurrentMapNames> _cachedDataProvider = cachedDataProvider;

    public async Task<CurrentMapNames> GetAsync()
    {
        try
        {
            return await _cachedDataProvider.GetCachedOrRequestAsync(FetchCurrentMapNamesAsync, null);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Current map names could not be loaded from matchmaking; showing stored names for {Duration}", FailureCacheDuration);
            // The failed fetch left no entry, so this stores Empty briefly (or returns names another request stored meanwhile).
            return await _cachedDataProvider.GetCachedOrRequestAsync(() => Task.FromResult(CurrentMapNames.Empty), null, FailureCacheDuration);
        }
    }

    private async Task<CurrentMapNames> FetchCurrentMapNamesAsync()
    {
        var maps = await _matchmakingServiceClient.GetAllMapsOrThrow();
        return CurrentMapNames.From(maps.Items);
    }
}
