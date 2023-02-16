using MongoDB.Driver;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Cache;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;

namespace W3ChampionsStatisticService.Services
{
    public class MatchmakingProvider : MongoDbRepositoryBase
    {
        private readonly MatchmakingServiceClient _matchmakingServiceClient;
        private readonly ICacheData<GetSeasonMapsResponse> _cacheData;

        public MatchmakingProvider(MongoClient mongoClient, MatchmakingServiceClient matchmakingServiceClient, ICacheData<GetSeasonMapsResponse> cacheData) : base(mongoClient)
        {
            _matchmakingServiceClient = matchmakingServiceClient;
            _cacheData = cacheData;
        }
        public Task<GetSeasonMapsResponse> GetCurrentSeasonMapsAsync()
        {
            return _cacheData.GetCachedOrRequestAsync(
                async () => await _matchmakingServiceClient
                .GetCurrentSeasonMaps(), null);
        }
    }
}