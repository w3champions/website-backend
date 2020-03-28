using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.MongoDb
{
    public class VersionRepository : IVersionRepository
    {
        private readonly DbConnctionInfo _connectionInfo;
        private readonly string _databaseName = "W3Champions-Statistic-Service";
        private readonly string _collection = "MatchFinishedEvents";

        public VersionRepository(DbConnctionInfo connectionInfo)
        {
            _connectionInfo = connectionInfo;
        }

        public async Task<string> GetLastVersion<T>()
        {
            var database = CreateClient();
            var mongoCollection = database.GetCollection<VersionDto>(_collection);
            var version = (await mongoCollection.FindAsync(c => c.HandlerName == nameof(T))).FirstOrDefaultAsync()?.Result?.LastVersion;
            return version ?? ObjectId.Empty.ToString();
        }

        public async Task SaveLastVersion<T>(string lastVersion)
        {
            var database = CreateClient();
            var mongoCollection = database.GetCollection<VersionDto>(_collection);
            var newVersion = new VersionDto {HandlerName = nameof(T), LastVersion = lastVersion};
            await mongoCollection.ReplaceOneAsync(
                new BsonDocument("_id", nameof(T)),
                options: new UpdateOptions { IsUpsert = true },
                replacement: newVersion);
        }

        private IMongoDatabase CreateClient()
        {
            var client = new MongoClient(_connectionInfo.ConnectionString);
            var database = client.GetDatabase(_databaseName);
            return database;
        }
    }

    public class VersionDto
    {
        public string Id => HandlerName;
        public string HandlerName { get; set; }
        public string LastVersion { get; set; }
    }
}