using MongoDB.Driver;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Cache;
using W3C.Domain.MatchmakingService;
using System.Collections.Generic;

namespace W3ChampionsStatisticService.Services;

// TODO: It is a repository with a cache
public class MatchmakingProvider(
    MongoClient mongoClient,
    MatchmakingServiceClient matchmakingServiceClient,
    ICachedDataProvider<List<ActiveGameMode>> cachedDataProvider
    ) : MongoDbRepositoryBase(mongoClient)
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
