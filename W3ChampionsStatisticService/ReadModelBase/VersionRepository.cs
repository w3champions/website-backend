using MongoDB.Bson;
using MongoDB.Driver;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.ReadModelBase;

[Trace]
public class VersionRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IVersionRepository
{
    private readonly string _collection = "HandlerVersions";

    public async Task<HandlerVersion> GetLastVersion<T>()
    {
        var database = CreateClient();
        var mongoCollection = database.GetCollection<VersionDto>(_collection);
        var version = (await mongoCollection.FindAsync(c =>
            c.HandlerName == HandlerName<T>()))
            .FirstOrDefaultAsync()?
            .Result;
        var lastVersion = version?.LastVersion ?? ObjectId.Empty.ToString();
        return new HandlerVersion(lastVersion, version?.Season ?? 0, version?.Stopped ?? false);
    }

    public async Task SaveLastVersion<T>(string lastVersion, int season = 0)
    {
        var database = CreateClient();
        var mongoCollection = database.GetCollection<VersionDto>(_collection);
        var version = await mongoCollection.Find(e => e.HandlerName == HandlerName<T>()).FirstOrDefaultAsync();
        if (version != null)
        {
            var filterDefinition = Builders<VersionDto>.Filter.Eq(e => e.HandlerName, HandlerName<T>());
            var updateDefinition = Builders<VersionDto>.Update
                .Set(e => e.LastVersion, lastVersion)
                .Set(e => e.Season, season);
            await mongoCollection.UpdateOneAsync(filterDefinition, updateDefinition);
        }
        else
        {
            await mongoCollection.InsertOneAsync(new VersionDto
            {
                LastVersion = lastVersion,
                Season = season,
                HandlerName = HandlerName<T>()
            });
        }
    }

    [NoTrace]
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
