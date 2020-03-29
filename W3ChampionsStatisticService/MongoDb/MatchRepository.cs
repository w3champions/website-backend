using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.MongoDb
{
    public class MatchRepository : MongoDbRepositoryBase, IMatchRepository
    {
        public MatchRepository(DbConnctionInfo connctionInfo) : base(connctionInfo)
        {
        }

        public async Task Insert(Matchup matchup)
        {
            var mongoDatabase = CreateClient();
            var mongoCollection = mongoDatabase.GetCollection<Matchup>(nameof(Matchup));

            await mongoCollection.InsertOneAsync(matchup);
        }

        public Task<List<Matchup>> Load(string lastObjectId = null, int pageSize = 100)
        {
            return Load<Matchup>(lastObjectId, pageSize);
        }
    }
}