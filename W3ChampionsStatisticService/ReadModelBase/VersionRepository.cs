using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.ReadModelBase;

public class VersionRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IVersionRepository
{
    private readonly string _collection = "HandlerVersions";

    [NoTrace]
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

    [NoTrace]
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

            // Any forward progress means the previously failing event is behind us. The fields are
            // removed rather than zeroed, and only when they are actually present, so a document looks
            // exactly like it did before this feature existed whenever a handler is healthy.
            if (version.FailingEventId != null || version.FailureCount != 0)
            {
                updateDefinition = updateDefinition
                    .Unset(e => e.FailingEventId)
                    .Unset(e => e.FailureCount);
            }

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

    // Read-modify-write rather than an atomic $inc, matching SaveLastVersion above. This is safe because
    // a handler type is driven by exactly one sequential AsyncServiceBase loop, which is the same
    // single-writer assumption the watermark itself already relies on. If read models are ever processed
    // by concurrent instances, this needs to become an atomic pipeline update - a lost update here would
    // undercount failures and delay the skip, so it degrades gracefully rather than skipping too early.
    [NoTrace]
    public async Task<int> RecordEventFailure<T>(string eventId)
    {
        var database = CreateClient();
        var mongoCollection = database.GetCollection<VersionDto>(_collection);
        var version = await mongoCollection.Find(e => e.HandlerName == HandlerName<T>()).FirstOrDefaultAsync();

        // A different event than the last recorded one means the handler moved on in between, so the
        // retry budget starts over for this event.
        var failureCount = version?.FailingEventId == eventId ? version.FailureCount + 1 : 1;

        if (version != null)
        {
            var filterDefinition = Builders<VersionDto>.Filter.Eq(e => e.HandlerName, HandlerName<T>());
            var updateDefinition = Builders<VersionDto>.Update
                .Set(e => e.FailingEventId, eventId)
                .Set(e => e.FailureCount, failureCount);
            await mongoCollection.UpdateOneAsync(filterDefinition, updateDefinition);
        }
        else
        {
            // The very first event a handler ever sees can fail before a watermark was ever written.
            await mongoCollection.InsertOneAsync(new VersionDto
            {
                LastVersion = ObjectId.Empty.ToString(),
                HandlerName = HandlerName<T>(),
                FailingEventId = eventId,
                FailureCount = failureCount
            });
        }

        return failureCount;
    }

    [NoTrace]
    private static string HandlerName<T>()
    {
        return typeof(T).Name;
    }
}

[BsonIgnoreExtraElements]
public class VersionDto
{
    public string Id => HandlerName;
    public string HandlerName { get; set; }
    public string LastVersion { get; set; }
    public bool Stopped { get; set; } = false;
    public int Season { get; set; }

    /// <summary>The event this handler is currently stuck on, or null when it is making progress.</summary>
    public string FailingEventId { get; set; }

    /// <summary>How often <see cref="FailingEventId"/> has failed in a row. See MaxEventFailuresBeforeSkip.</summary>
    public int FailureCount { get; set; }
}
