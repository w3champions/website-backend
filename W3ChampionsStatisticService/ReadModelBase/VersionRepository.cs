using MongoDB.Bson;
using MongoDB.Driver;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.ReadModelBase;

public class VersionRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IVersionRepository
{
    private readonly string _collection = "HandlerVersions";

    public async Task<HandlerVersion> GetLastVersion<T>()
    {
        var targetHandlerName = HandlerName<T>();
        var version = await LoadFirst(Builders<VersionDto>.Filter.Eq(c => c.HandlerName, targetHandlerName));
        var lastVersion = version?.LastVersion ?? ObjectId.Empty.ToString();
        return new HandlerVersion(lastVersion, version?.Season ?? 0, version?.Stopped ?? false);
    }

    public async Task SaveLastVersion<T>(string lastVersion, int season = 0)
    {
        var filterDefinition = Builders<VersionDto>.Filter.Eq(e => e.HandlerName, HandlerName<T>());
        var updateDefinition = Builders<VersionDto>.Update
            .Set(e => e.LastVersion, lastVersion)
            .Set(e => e.Season, season);

        await UpdateOneAsync(
            filterDefinition,
            updateDefinition,
            new UpdateOptions { IsUpsert = true });
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
    public bool Stopped { get; set; } = false;
    public int Season { get; set; }
}
