using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.MongoDb
{
    public class VersionRepository : MongoDbRepositoryBase,  IVersionRepository
    {
        private readonly string _collection = "MatchFinishedEvents";

        public VersionRepository(DbConnctionInfo connectionInfo) : base(connectionInfo)
        {
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
                options: new ReplaceOptions { IsUpsert = true },
                replacement: newVersion);
        }
    }

    public class VersionDto
    {
        public string Id => HandlerName;
        public string HandlerName { get; set; }
        public string LastVersion { get; set; }
    }
}