using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Ports;
using InsertManyOptions = MongoDB.Driver.InsertManyOptions;
using UpdateOptions = MongoDB.Driver.UpdateOptions;

namespace W3ChampionsStatisticService.MongoDb
{
    public class MatchRepository : MongoDbRepositoryBase, IMatchRepository
    {
        private readonly string _matches = "Matches";

        public async Task<string> Upsert(List<Matchup> matchups)
        {
            var mongoDatabase = CreateClient();
            var mongoCollection = mongoDatabase.GetCollection<Matchup>(_matches);

            foreach (var matchup in matchups)
            {
                await mongoCollection.ReplaceOneAsync(
                    filter: new BsonDocument("_id", matchup.Id),
                    options: new ReplaceOptions { IsUpsert = true },
                    replacement: matchup);
            }

            return matchups.Last().Id.ToString();
        }

        public MatchRepository(DbConnctionInfo connctionInfo) : base(connctionInfo)
        {
        }

        public Task<List<Matchup>> Load(string lastObjectId = null, int pageSize = 100)
        {
            return Load<Matchup>(_matches, lastObjectId, pageSize);
        }
    }
}