using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.MongoDb
{
    public class VersionRepository : MongoDbRepositoryBase,  IVersionRepository
    {
        private readonly string _collection = "HandlerVersions";

        public VersionRepository(DbConnctionInfo connectionInfo) : base(connectionInfo)
        {
        }

        public async Task<string> GetLastVersion<T>()
        {
            var database = CreateClient();
            var mongoCollection = database.GetCollection<VersionDto>(_collection);
            var version = (await mongoCollection.FindAsync(c => c.HandlerName == HandlerName<T>()))
                .FirstOrDefaultAsync()?
                .Result?
                .LastVersion;
            return version ?? ObjectId.Empty.ToString();
        }

        public async Task SaveLastVersion<T>(string lastVersion)
        {
            var database = CreateClient();
            var mongoCollection = database.GetCollection<VersionDto>(_collection);
            var newVersion = new VersionDto {HandlerName = HandlerName<T>(), LastVersion = lastVersion};
            await mongoCollection.ReplaceOneAsync(
                new BsonDocument("_id", HandlerName<T>()),
                options: new ReplaceOptions { IsUpsert = true },
                replacement: newVersion);
        }

        public async Task ResetVersion(string readModelType)
        {
            var database = CreateClient();
            var mongoCollection = database.GetCollection<VersionDto>(_collection);
            var newVersion = new VersionDto {HandlerName = readModelType, LastVersion = ObjectId.Empty.ToString()};
            await mongoCollection.ReplaceOneAsync(
                new BsonDocument("_id", readModelType),
                options: new ReplaceOptions { IsUpsert = true },
                replacement: newVersion);
        }

        private static string HandlerName<T>()
        {
            return typeof(T).Name;
        }
    }

    public class VersionDto
    {
        public string Id => HandlerName;
        public string HandlerName { get; set; }
        public string LastVersion { get; set; }
    }
}