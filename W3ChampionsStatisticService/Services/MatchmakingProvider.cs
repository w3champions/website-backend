using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.PersonalSettings;
using W3C.Domain.MatchmakingService.MatchmakingContracts;
using W3C.Domain.MatchmakingService;

namespace W3ChampionsStatisticService.Services
{
    public class MatchmakingProvider : MongoDbRepositoryBase
    {
        private readonly MatchmakingServiceClient _matchmakingServiceClient;
        private static CachedData<GetSeasonMapsResponse> _currentSeasonMapsCache;

        public MatchmakingProvider(
          MongoClient mongoClient,
          MatchmakingServiceClient matchmakingServiceClient) : base(mongoClient)
        {
            _matchmakingServiceClient = matchmakingServiceClient;
            _currentSeasonMapsCache = new CachedData<GetSeasonMapsResponse>(() => FetchSeasonMapsResponse(), TimeSpan.FromMinutes(10));
        }

        private GetSeasonMapsResponse FetchSeasonMapsResponse()
        {
            return _matchmakingServiceClient
              .GetCurrentSeasonMaps()
              .GetAwaiter()
              .GetResult();
        }

        public GetSeasonMapsResponse GetCurrentSeasonMaps()
        {
            return _currentSeasonMapsCache.GetCachedData();
        }
    }
}
