using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.MatchEvents
{
    public class MatchEventRepository : MongoDbRepositoryBase, IMatchEventRepository
    {
        public MatchEventRepository(DbConnctionInfo connectionInfo) : base(connectionInfo)
        {
        }

        public async Task Insert(List<MatchFinishedEvent> events)
        {
            await InsertPadEvents(events);
        }

        private async Task InsertPadEvents<T>(List<T> events) where T : PadEvent
        {
            if (!events.Any()) return;
            var database = CreateClient();

            var mongoCollection = database.GetCollection<T>(typeof(T).Name);
            await mongoCollection.InsertManyAsync(events);
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

        public async Task Insert(List<MatchStartedEvent> events)
        {
            await InsertPadEvents(events);
        }

        public async Task Insert(List<LeagueConstellationChangedEvent> events)
        {
            foreach (var ev in events)
            {
                await Upsert(ev, r => r.gateway == ev.gateway);
            }
        }

        public async Task Insert(List<RankingChangedEvent> events)
        {
            foreach (var ev in events)
            {
                await Upsert(ev, r => r.league == ev.league);
            }
        }

        public Task<RankingChangedEvent> LoadRank(int ladderId, int gateWay)
        {
            return LoadFirst<RankingChangedEvent>(r => r.gateway == gateWay && r.league == ladderId);
        }

        public Task<List<LeagueConstellationChangedEvent>> LoadLeagues()
        {
            return LoadAll<LeagueConstellationChangedEvent>();
        }

        public Task<List<RankingChangedEvent>> LoadRanks()
        {
            return LoadAll<RankingChangedEvent>();
        }
    }
}