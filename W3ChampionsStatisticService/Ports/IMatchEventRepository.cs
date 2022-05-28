using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using W3C.Domain.MatchmakingService;

namespace W3ChampionsStatisticService.Ports
{
    public interface IMatchEventRepository
    {
        Task<List<MatchFinishedEvent>> Load(string lastObjectId,  int pageSize = 100);
        Task<List<MatchStartedEvent>> LoadStartedMatches();
        Task<bool> InsertIfNotExisting(MatchFinishedEvent matchFinishedEvent, int i = 0);
        Task<List<RankingChangedEvent>> CheckoutForRead();
        Task<List<LeagueConstellationChangedEvent>> LoadLeagueConstellationChanged();
        Task DeleteStartedEvent(ObjectId nextEventId);
    }
}