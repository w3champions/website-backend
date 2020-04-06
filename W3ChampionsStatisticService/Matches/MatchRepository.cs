using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Matches
{
    public class MatchRepository : MongoDbRepositoryBase, IMatchRepository
    {
        public MatchRepository(DbConnctionInfo connectionInfo) : base(connectionInfo)
        {
        }

        public Task Insert(Matchup matchup)
        {
            return Upsert(matchup, m => m.Id == matchup.Id);
        }

        public async Task<List<Matchup>> LoadFor(string playerId, int gateWay = 10, int pageSize = 100, int offset = 0)
        {
            var database = CreateClient();

            var mongoCollection = database.GetCollection<Matchup>(nameof(Matchup));

            var strings = playerId.Split("#");
            if (strings.Length != 2) return new List<Matchup>();
            var name = strings[0];
            var battleTag = strings[1];

            var events = await mongoCollection
                .Find(m => m.GateWay == gateWay && m.Teams
                               .Any(t => t.Players
                                   .Any(p => p.Name == name && p.BattleTag == battleTag)))
                .SortBy(s => s.StartTime)
                .Skip(offset)
                .Limit(pageSize)
                .ToListAsync();

            return events;
        }

        public async Task<List<Matchup>> Load(
            int offset = 0,
            int pageSize = 100,
            int gateWay = 10)
        {
            var database = CreateClient();

            var mongoCollection = database.GetCollection<Matchup>(nameof(Matchup));

            var events = await mongoCollection.Find(m => m.GateWay == gateWay)
                .SortBy(s => s.StartTime)
                .Skip(offset)
                .Limit(pageSize)
                .ToListAsync();

            return events;
        }
    }
}