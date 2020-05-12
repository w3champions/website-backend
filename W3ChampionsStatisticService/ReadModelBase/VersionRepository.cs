using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.ReadModelBase
{
    public class VersionRepository : MongoDbRepositoryBase,  IVersionRepository
    {
        private readonly string _collection = "HandlerVersions";

        public VersionRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public async Task<HandlerVersion> GetLastVersion<T>()
        {
            var database = CreateClient();
            var mongoCollection = database.GetCollection<VersionDto>(_collection);
            var version = (await mongoCollection.FindAsync(c => c.HandlerName == HandlerName<T>()))
                .FirstOrDefaultAsync()?
                .Result;
            var lastVersion = version?.LastVersion ?? ObjectId.Empty.ToString();
            return new HandlerVersion(lastVersion, version?.Season ?? 0);
        }

        public async Task SaveLastVersion<T>(string lastVersion, int season)
        {
            var database = CreateClient();
            var mongoCollection = database.GetCollection<VersionDto>(_collection);
            var newVersion = new VersionDto {HandlerName = HandlerName<T>(), LastVersion = lastVersion, Season = season};
            await mongoCollection.ReplaceOneAsync(
                new BsonDocument("_id", HandlerName<T>()),
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
        public int Season { get; set; }
    }
}