using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PadEvents
{
    public class MatchEventRepository : MongoDbRepositoryBase, IMatchEventRepository
    {
        public MatchEventRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public async Task<List<MatchFinishedEvent>> Load(string lastObjectId = null, int pageSize = 100)
        {
            lastObjectId ??= ObjectId.Empty.ToString();
            var database = CreateClient();

            var mongoCollection = database.GetCollection<MatchFinishedEvent>(nameof(MatchFinishedEvent));

            var events = await mongoCollection.Find(m => m.Id > ObjectId.Parse(lastObjectId))
                .SortBy(s => s.Id)
                .Limit(pageSize)
                .ToListAsync();

            return events;
        }

        public Task<List<LeagueConstellationChangedEvent>> LoadLeagues()
        {
            return LoadAll<LeagueConstellationChangedEvent>();
        }

        public Task<List<RankingChangedEvent>> LoadRanks()
        {
            return LoadAll<RankingChangedEvent>();
        }

        public async Task<List<LeagueConstellationChangedEvent>> LoadLeagueConstellation()
        {
            var loadLeagueConstellation = await LoadAll<LeagueConstellationChangedEvent>();

            foreach (var leagueConstellationChangedEvent in loadLeagueConstellation)
            {
                leagueConstellationChangedEvent.leagues =
                    leagueConstellationChangedEvent.leagues.OrderBy(l => l.order).ToArray();
            }

            return loadLeagueConstellation;
        }
    }
}