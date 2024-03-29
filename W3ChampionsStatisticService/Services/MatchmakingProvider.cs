using MongoDB.Driver;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Cache;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using System.Collections.Generic;

namespace W3ChampionsStatisticService.Services;

// TODO: It is a repository with a cache
public class MatchmakingProvider : MongoDbRepositoryBase
{
    private readonly MatchmakingServiceClient _matchmakingServiceClient;
    private readonly ICachedDataProvider<List<ActiveGameMode>> _cachedDataProvider;

    public MatchmakingProvider(
        MongoClient mongoClient,
        MatchmakingServiceClient matchmakingServiceClient,
        ICachedDataProvider<List<ActiveGameMode>> cachedDataProvider
    ) : base(mongoClient)
    {
        _matchmakingServiceClient = matchmakingServiceClient;
        _cachedDataProvider = cachedDataProvider;
    }

    public Task<List<ActiveGameMode>> GetCurrentlyActiveGameModesAsync()
    {
        return _cachedDataProvider.GetCachedOrRequestAsync(
            async () => await _matchmakingServiceClient
            .GetCurrentlyActiveGameModes(), null);
    }
}
