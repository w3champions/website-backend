using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.MongoDb
{
    public class MatchRepository : MongoDbRepositoryBase, IMatchRepository
    {
        private readonly string _matches = "Matches";

        public MatchRepository(DbConnctionInfo connctionInfo) : base(connctionInfo)
        {
        }

        public async Task Insert(List<Matchup> matchups)
        {
            var mongoDatabase = CreateClient();
            var mongoCollection = mongoDatabase.GetCollection<Matchup>(_matches);

            await mongoCollection.InsertManyAsync(matchups);
        }

        public Task<List<Matchup>> Load(string lastObjectId = null, int pageSize = 100)
        {
            return Load<Matchup>(_matches, lastObjectId, pageSize);
        }
    }
}