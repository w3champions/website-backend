using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace W3ChampionsStatisticService.Tools.MapKeyBackfill;

// One row per distinct {match.map, match.mapId, match.mapName, match.season, match.gameMode} of MatchFinishedEvent.
public sealed record EventMapGroup(string Path, int? MapId, string MapName, int Season, int GameMode, long Count, ObjectId LastId)
{
    public string File { get; } = MapFileName.Of(Path);
    public string LegacyKey { get; } = LegacyMapKey.Compute(Path);
}

public sealed record EventMapCatalogHeader(string Database, IReadOnlyList<string> Hosts, DateTime CreatedAt, long Events, int Groups);

public sealed class EventMapCatalog(EventMapCatalogHeader header, IReadOnlyList<EventMapGroup> groups)
{
    public const string EventCollectionName = "MatchFinishedEvent";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public EventMapCatalogHeader Header { get; } = header;
    public IReadOnlyList<EventMapGroup> Groups { get; } = groups;

    public static EventMapCatalog Create(string database, IReadOnlyList<string> hosts, IEnumerable<EventMapGroup> groups)
    {
        var list = groups.ToList();
        return new EventMapCatalog(
            new EventMapCatalogHeader(database, hosts, DateTime.UtcNow, list.Sum(g => g.Count), list.Count),
            list);
    }

    // Aggregates one calendar month of events at a time (by the ObjectId timestamp of _id) and pauses between
    // months, so a single-server database isn't held by one long aggregation.
    public static async Task<EventMapCatalog> AggregateAsync(
        IMongoCollection<BsonDocument> events,
        string database,
        IReadOnlyList<string> hosts,
        TimeSpan pause,
        Action<string> log)
    {
        var idOnly = Builders<BsonDocument>.Projection.Include("_id");
        var first = await events.Find(FilterDefinition<BsonDocument>.Empty).Sort(Builders<BsonDocument>.Sort.Ascending("_id")).Project(idOnly).Limit(1).FirstOrDefaultAsync();
        var last = await events.Find(FilterDefinition<BsonDocument>.Empty).Sort(Builders<BsonDocument>.Sort.Descending("_id")).Project(idOnly).Limit(1).FirstOrDefaultAsync();
        if (first == null || last == null)
        {
            log($"{EventCollectionName} is empty.");
            return Create(database, hosts, Array.Empty<EventMapGroup>());
        }

        var merged = new Dictionary<GroupKey, (long Count, ObjectId LastId)>();
        var firstTime = first["_id"].AsObjectId.CreationTime;
        var lastTime = last["_id"].AsObjectId.CreationTime;
        var start = new DateTime(firstTime.Year, firstTime.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        while (start <= lastTime)
        {
            var end = start.AddMonths(1);
            var docs = await AggregateRangeAsync(events, MinObjectId(start), MinObjectId(end));
            long monthEvents = 0;
            foreach (var doc in docs)
            {
                var key = ReadGroupKey(doc["_id"].AsBsonDocument);
                var count = doc["count"].ToInt64();
                var lastId = doc["lastId"].AsObjectId;
                monthEvents += count;
                merged[key] = merged.TryGetValue(key, out var existing)
                    ? (existing.Count + count, existing.LastId > lastId ? existing.LastId : lastId)
                    : (count, lastId);
            }

            log($"  {start:yyyy-MM}: {monthEvents} events, {docs.Count} groups");
            start = end;
            if (start <= lastTime && pause > TimeSpan.Zero)
            {
                await Task.Delay(pause);
            }
        }

        return Create(
            database,
            hosts,
            merged.Select(m => new EventMapGroup(m.Key.Map, m.Key.MapId, m.Key.MapName, m.Key.Season, m.Key.GameMode, m.Value.Count, m.Value.LastId)));
    }

    public void Save(string path)
    {
        using var writer = new StreamWriter(File.Create(path), new UTF8Encoding(false));
        writer.WriteLine(JsonSerializer.Serialize(new CatalogLine
        {
            Type = "header",
            Database = Header.Database,
            Hosts = Header.Hosts?.ToList(),
            CreatedAt = Header.CreatedAt,
            Events = Header.Events,
            Groups = Header.Groups
        }, JsonOptions));

        foreach (var group in Groups.OrderBy(g => g.Season).ThenBy(g => g.GameMode).ThenBy(g => g.Path, StringComparer.Ordinal).ThenBy(g => g.LastId))
        {
            writer.WriteLine(JsonSerializer.Serialize(new CatalogLine
            {
                Map = group.Path,
                MapId = group.MapId,
                MapName = group.MapName,
                Season = group.Season,
                GameMode = group.GameMode,
                Count = group.Count,
                LastId = group.LastId.ToString()
            }, JsonOptions));
        }
    }

    public static EventMapCatalog Load(string path)
    {
        var lines = File.ReadLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0)
        {
            throw new InvalidDataException($"Event catalog '{path}' is empty.");
        }

        var header = JsonSerializer.Deserialize<CatalogLine>(lines[0], JsonOptions);
        if (header?.Type != "header")
        {
            throw new InvalidDataException($"Event catalog '{path}' doesn't start with a header line.");
        }

        var groups = new List<EventMapGroup>();
        for (var i = 1; i < lines.Count; i++)
        {
            var line = JsonSerializer.Deserialize<CatalogLine>(lines[i], JsonOptions);
            if (line?.Map == null || line.Count == null || !ObjectId.TryParse(line.LastId, out var lastId))
            {
                throw new InvalidDataException($"Event catalog '{path}', line {i + 1}: map, count and lastId are required.");
            }

            groups.Add(new EventMapGroup(line.Map, line.MapId, line.MapName, line.Season ?? 0, line.GameMode ?? 0, line.Count.Value, lastId));
        }

        if (header.Groups != null && header.Groups.Value != groups.Count)
        {
            throw new InvalidDataException($"Event catalog '{path}' declares {header.Groups} groups but holds {groups.Count}.");
        }

        return new EventMapCatalog(
            new EventMapCatalogHeader(header.Database, header.Hosts ?? new List<string>(), header.CreatedAt ?? DateTime.MinValue, header.Events ?? groups.Sum(g => g.Count), groups.Count),
            groups);
    }

    private static async Task<List<BsonDocument>> AggregateRangeAsync(IMongoCollection<BsonDocument> events, ObjectId from, ObjectId to)
    {
        var stages = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                ["_id"] = new BsonDocument { ["$gte"] = from, ["$lt"] = to },
                ["match.map"] = new BsonDocument("$type", "string")
            }),
            new BsonDocument("$group", new BsonDocument
            {
                ["_id"] = new BsonDocument
                {
                    ["map"] = "$match.map",
                    ["mapId"] = "$match.mapId",
                    ["mapName"] = "$match.mapName",
                    ["season"] = "$match.season",
                    ["gameMode"] = "$match.gameMode"
                },
                ["count"] = new BsonDocument("$sum", 1),
                ["lastId"] = new BsonDocument("$max", "$_id")
            })
        };

        var options = new AggregateOptions { AllowDiskUse = true, MaxTime = TimeSpan.FromMinutes(30) };
        using var cursor = await events.AggregateAsync(PipelineDefinition<BsonDocument, BsonDocument>.Create(stages), options);
        return await cursor.ToListAsync();
    }

    private static GroupKey ReadGroupKey(BsonDocument id)
    {
        // Events from before 2020-05 have no season; their read models were stored as season 0.
        return new GroupKey(
            id.GetValue("map", BsonNull.Value).AsString,
            ReadNullableInt(id, "mapId"),
            id.GetValue("mapName", BsonNull.Value) is BsonString name ? name.Value : null,
            ReadNullableInt(id, "season") ?? 0,
            ReadNullableInt(id, "gameMode") ?? GameModeNames.Undefined);
    }

    private static int? ReadNullableInt(BsonDocument doc, string name)
    {
        var value = doc.GetValue(name, BsonNull.Value);
        return value.IsNumeric ? value.ToInt32() : null;
    }

    public static ObjectId MinObjectId(DateTime utc)
    {
        var seconds = (uint)(utc - DateTime.UnixEpoch).TotalSeconds;
        var bytes = new byte[12];
        bytes[0] = (byte)(seconds >> 24);
        bytes[1] = (byte)(seconds >> 16);
        bytes[2] = (byte)(seconds >> 8);
        bytes[3] = (byte)seconds;
        return new ObjectId(bytes);
    }

    private sealed record GroupKey(string Map, int? MapId, string MapName, int Season, int GameMode);

    private sealed class CatalogLine
    {
        public string Type { get; set; }
        public string Database { get; set; }
        public List<string> Hosts { get; set; }
        public DateTime? CreatedAt { get; set; }
        public long? Events { get; set; }
        public int? Groups { get; set; }
        public string Map { get; set; }
        public int? MapId { get; set; }
        public string MapName { get; set; }
        public int? Season { get; set; }
        public int? GameMode { get; set; }
        public long? Count { get; set; }
        public string LastId { get; set; }
    }
}
