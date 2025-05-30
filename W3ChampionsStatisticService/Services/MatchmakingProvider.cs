using MongoDB.Driver;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Cache;
using W3C.Domain.MatchmakingService;
using System.Collections.Generic;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Services;

public interface IMatchmakingProvider
{
    Task<List<ActiveGameMode>> GetCurrentlyActiveGameModesAsync();
}

// TODO: It is a repository with a cache
[Trace]
public class MatchmakingProvider(
    MongoClient mongoClient,
    MatchmakingServiceClient matchmakingServiceClient,
    ICachedDataProvider<List<ActiveGameMode>> cachedDataProvider
    ) : MongoDbRepositoryBase(mongoClient), IMatchmakingProvider
{
    private readonly MatchmakingServiceClient _matchmakingServiceClient = matchmakingServiceClient;
    private readonly ICachedDataProvider<List<ActiveGameMode>> _cachedDataProvider = cachedDataProvider;

    public Task<List<ActiveGameMode>> GetCurrentlyActiveGameModesAsync()
    {
        return _cachedDataProvider.GetCachedOrRequestAsync(
            async () => await _matchmakingServiceClient
            .GetCurrentlyActiveGameModes(), null);
    }
}
