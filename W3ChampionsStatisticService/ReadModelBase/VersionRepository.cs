using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.ReadModelBase
{
    public class VersionRepository : MongoDbRepositoryBase, IVersionRepository
    {
        private readonly string _collection = "HandlerVersions";

        public VersionRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public async Task<HandlerVersion> GetLastVersion<T>(bool tempVersion = false)
        {
            var database = CreateClient();
            var mongoCollection = database.GetCollection<VersionDto>(_collection);
            var version = (await mongoCollection.FindAsync(c =>
                c.HandlerName == (tempVersion
                    ? HandlerName<T>()
                    : TempHandlerName<T>())))
                .FirstOrDefaultAsync()?
                .Result;
            var lastVersion = version?.LastVersion ?? ObjectId.Empty.ToString();
            return new HandlerVersion(lastVersion, version?.Season ?? 0, version?.Stopped ?? false,
                version?.SyncState ?? SyncState.UpToDate);
        }

        public async Task SaveLastVersion<T>(string lastVersion, int season = 0, bool tempVersion = false)
        {
            var database = CreateClient();
            var mongoCollection = database.GetCollection<VersionDto>(_collection);
            var version = await mongoCollection.Find(e => e.HandlerName == (tempVersion
                ? HandlerName<T>()
                : TempHandlerName<T>())).FirstOrDefaultAsync();
            if (version != null)
            {
                var filterDefinition = Builders<VersionDto>.Filter.Eq(e => e.HandlerName,
                    tempVersion
                        ? HandlerName<T>()
                        : TempHandlerName<T>());
                var updateDefinition = Builders<VersionDto>.Update
                    .Set(e => e.LastVersion, lastVersion)
                    .Set(e => e.Season, season);
                await mongoCollection.UpdateOneAsync(filterDefinition, updateDefinition);
            }
            else
            {
                await mongoCollection.InsertOneAsync(new VersionDto
                {
                    LastVersion = lastVersion, Season = season, HandlerName =
                        tempVersion
                            ? HandlerName<T>()
                            : TempHandlerName<T>()
                });
            }
        }

        public async Task SaveSyncState<T>(SyncState syncState, bool tempVersion = false)
        {
            var database = CreateClient();
            var mongoCollection = database.GetCollection<VersionDto>(_collection);

            var filterDefinition = Builders<VersionDto>.Filter.Eq(e => e.HandlerName,
                tempVersion
                    ? HandlerName<T>()
                    : TempHandlerName<T>());
            var updateDefinition = Builders<VersionDto>.Update
                .Set(e => e.SyncState, syncState);
            await mongoCollection.UpdateOneAsync(filterDefinition, updateDefinition);
        }

        private static string HandlerName<T>()
        {
            return typeof(T).Name;
        }

        private static string TempHandlerName<T>()
        {
            return typeof(T).Name + "_temp";
        }
    }

    public class VersionDto
    {
        public string Id => HandlerName;
        public string HandlerName { get; set; }
        public string LastVersion { get; set; }
        public bool Stopped { get; set; } = false;
        public SyncState SyncState { get; set; } = SyncState.UpToDate;
        public int Season { get; set; }
    }

    public enum SyncState
    {
        UpToDate = 0,
        SyncStartRequested = 1,
        ParallelSyncStarted = 2,
        Syncing = 3,
        SyncUpToOriginalSync = 4
    }
}