using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.Ports
{
    public interface IMatchEventRepository
    {
        Task<List<MatchFinishedEvent>> Load(string lastObjectId,  int pageSize = 100);
        Task InsertIfNotExisting(MatchFinishedEvent matchFinishedEvent);
        Task<List<RankingChangedEvent>> LoadRanks();
    }
}