using MongoDB.Driver;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Cache;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using System.Collections.Generic;

namespace W3ChampionsStatisticService.Services
{
    // TODO: It is a repository with a cache
    public class MatchmakingProvider : MongoDbRepositoryBase
    {
        private readonly MatchmakingServiceClient _matchmakingServiceClient;
        private readonly ICachedDataProvider<GetSeasonMapsResponse> _cachedDataProvider;
        private readonly ICachedDataProvider<List<ActiveGameMode>> _cachedDataProvider2;

        public MatchmakingProvider(
            MongoClient mongoClient,
            MatchmakingServiceClient matchmakingServiceClient,
            ICachedDataProvider<GetSeasonMapsResponse> cachedDataProvider,
            ICachedDataProvider<List<ActiveGameMode>> cachedDataProvider2
        ) : base(mongoClient)
        {
            _matchmakingServiceClient = matchmakingServiceClient;
            _cachedDataProvider = cachedDataProvider;
            _cachedDataProvider2 = cachedDataProvider2;
        }
        public Task<GetSeasonMapsResponse> GetCurrentSeasonMapsAsync()
        {
            return _cachedDataProvider.GetCachedOrRequestAsync(
                async () => await _matchmakingServiceClient
                .GetCurrentSeasonMaps(), null);
        }

        public Task<List<ActiveGameMode>> GetCurrentlyActiveGameModesAsync()
        {
            return _cachedDataProvider2.GetCachedOrRequestAsync(
                async () => await _matchmakingServiceClient
                .GetCurrentlyActiveGameModes(), null);
        }
    }
}
