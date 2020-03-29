using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace W3ChampionsStatisticService.MongoDb
{
    public class MongoDbRepositoryBase
    {
        private readonly DbConnctionInfo _connectionInfo;
        private readonly string _databaseName = "W3Champions-Statistic-Service";

        public MongoDbRepositoryBase(DbConnctionInfo connctionInfo)
        {
            _connectionInfo = connctionInfo;
        }

        protected IMongoDatabase CreateClient()
        {
            var client = new MongoClient(_connectionInfo.ConnectionString);
            var database = client.GetDatabase(_databaseName);
            return database;
        }

        protected async Task<List<T>> Load<T>(string collectionName, string lastObjectId, int pageSize) where T : Identifiable
        {
            lastObjectId ??= ObjectId.Empty.ToString();
            var database = CreateClient();

            var mongoCollection = database.GetCollection<T>(collectionName);
            var filterBuilder = Builders<T>.Filter;
            var filter = filterBuilder.Gt(x => x.Id, ObjectId.Parse(lastObjectId));

            var events = await mongoCollection.Find(filter)
                .SortBy(s => s.Id)
                .Limit(pageSize)
                .ToListAsync();

            return events;
        }
    }
}