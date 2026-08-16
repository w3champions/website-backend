using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.LagReports;

[Trace]
public class LagReportRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IRequiresIndexes
{
    public string CollectionName => "LagReport";

    public async Task EnsureIndexesAsync()
    {
        var collection = CreateCollection<LagReport>();

        var indexes = new List<CreateIndexModel<LagReport>>
        {
            // Upsert lookup + game filter — one report per flo game
            new(Builders<LagReport>.IndexKeys.Ascending(r => r.FloGameId),
                new CreateIndexOptions { Unique = true }),

            // W3C match ID filter
            new(Builders<LagReport>.IndexKeys.Ascending(r => r.GameId),
                new CreateIndexOptions { Unique = true }),

            // Server filter
            new(Builders<LagReport>.IndexKeys.Ascending(r => r.ServerNodeId)),

            // Case-insensitive prefix search: the admin list filters on lowercased shadow
            // fields with an anchored /^.../ regex so the index can bound the scan. A
            // case-insensitive regex on the raw field cannot, and fetches every document.
            new(Builders<LagReport>.IndexKeys.Ascending(r => r.GameNameSearch)),
            new(Builders<LagReport>.IndexKeys.Ascending(r => r.ServerNodeNameSearch)),
            new(Builders<LagReport>.IndexKeys.Ascending("Players.BattleTagSearch")),
            new(Builders<LagReport>.IndexKeys.Ascending("Players.ProxyNameSearch")),
            new(Builders<LagReport>.IndexKeys.Ascending("Players.ProxyIpSearch")),

            // Issue category filter (inside nested array)
            new(Builders<LagReport>.IndexKeys.Ascending("Players.IssueCategories")),

            // System-derived tag filter (inside nested array), mirrors IssueCategories
            new(Builders<LagReport>.IndexKeys.Ascending("Players.ConnectionIssueTags")),

            // Explicit filter — most reports are auto-submitted, admins typically filter to explicit only
            new(Builders<LagReport>.IndexKeys.Ascending(r => r.HasExplicitReport)),

            // Player-count filter (materialized Players.Count)
            new(Builders<LagReport>.IndexKeys.Ascending(r => r.PlayerCount)),

            // Default list sort + date range filter
            new(Builders<LagReport>.IndexKeys.Descending(r => r.CreatedAt)),

            // TTL: auto-expire documents 90 days after last update
            new(Builders<LagReport>.IndexKeys.Ascending(r => r.UpdatedAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(90) }),
        };

        await collection.Indexes.CreateManyAsync(indexes);

        // Drop the raw-field text indexes superseded by the lowercased *Search indexes above:
        // the list view now filters on the prefix-searchable shadow fields, so these are dead
        // write overhead. Tolerate "already dropped" so this stays idempotent across restarts.
        var obsoleteIndexes = new[]
        {
            "ServerNodeName_1", "GameName_1", "Players.BattleTag_1", "Players.ProxyName_1", "Players.ProxyIp_1",
        };
        foreach (var name in obsoleteIndexes)
        {
            try
            {
                await collection.Indexes.DropOneAsync(name);
            }
            catch (MongoCommandException ex) when (ex.Code == 27 || ex.CodeName == "IndexNotFound")
            {
                // Index doesn't exist (fresh DB or already dropped) — nothing to do.
            }
        }
    }

    /// <summary>
    /// Upsert a player's data into the per-game lag report document.
    /// Creates the document on first player submission; subsequent players
    /// are pushed into the Players array.
    /// Returns the document ID.
    /// </summary>
    public async Task<string> UpsertPlayerData(int floGameId, LagReportPlayer playerData, LagReport template)
    {
        var collection = CreateCollection<LagReport>();

        var filter = Builders<LagReport>.Filter.Eq(r => r.FloGameId, floGameId);

        // Maintain lowercased search fields so the admin list can do index-backed
        // case-insensitive prefix search (see BuildFilters / EnsureIndexesAsync).
        playerData.BattleTagSearch = playerData.BattleTag?.ToLowerInvariant();
        playerData.ProxyNameSearch = playerData.ProxyName?.ToLowerInvariant();
        playerData.ProxyIpSearch = playerData.ProxyIp?.ToLowerInvariant();

        // Atomic upsert: push the player and set UpdatedAt on every call,
        // $setOnInsert the template fields only when creating a new document.
        var update = Builders<LagReport>.Update
            .Push(r => r.Players, playerData)
            // Materialized Players.Count, atomic with the push — Mongo cannot filter on
            // an array's length, so the min/maxPlayers filters read this field instead.
            .Inc(r => r.PlayerCount, 1)
            .Set(r => r.UpdatedAt, DateTime.UtcNow)
            .SetOnInsert(r => r.Id, template.Id)
            .SetOnInsert(r => r.GameId, template.GameId)
            .SetOnInsert(r => r.FloGameId, template.FloGameId)
            .SetOnInsert(r => r.GameName, template.GameName)
            .SetOnInsert(r => r.GameNameSearch, template.GameName?.ToLowerInvariant())
            .SetOnInsert(r => r.MapPath, template.MapPath)
            .SetOnInsert(r => r.ServerNodeId, template.ServerNodeId)
            .SetOnInsert(r => r.ServerNodeName, template.ServerNodeName)
            .SetOnInsert(r => r.ServerNodeNameSearch, template.ServerNodeName?.ToLowerInvariant())
            .SetOnInsert(r => r.CreatedAt, DateTime.UtcNow);

        if (playerData.IsExplicit)
        {
            update = update.Set(r => r.HasExplicitReport, true);
        }

        var result = await collection.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<LagReport>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After,
                Projection = Builders<LagReport>.Projection.Include(r => r.Id),
            }
        );

        return result.Id;
    }

    public async Task<LagReport> GetById(string id)
    {
        return await LoadFirst<LagReport>(r => r.Id == id);
    }

    public async Task<LagReport> GetByFloGameId(int floGameId)
    {
        return await LoadFirst<LagReport>(r => r.FloGameId == floGameId);
    }

    public async Task UpdateServerSidePing(string reportId, List<ServerSidePingData> pingData)
    {
        var collection = CreateCollection<LagReport>();
        var filter = Builders<LagReport>.Filter.Eq(r => r.Id, reportId);
        var update = Builders<LagReport>.Update
            .Set(r => r.ServerSidePing, pingData)
            .Set(r => r.UpdatedAt, DateTime.UtcNow);
        await collection.UpdateOneAsync(filter, update);
    }

    public async Task<(List<LagReport> Items, long Total)> GetReports(LagReportQueryRequest req)
    {
        var collection = CreateCollection<LagReport>();
        var filters = BuildFilters(req);
        var hasFilter = filters.Count > 0;
        var filter = hasFilter ? Builders<LagReport>.Filter.And(filters) : Builders<LagReport>.Filter.Empty;

        // CountDocuments on an empty filter forces a full collection scan: the driver
        // runs it as a {$group} aggregation rather than the O(1) metadata count, which
        // dominates the response time of the common unfiltered list view. Use the fast
        // metadata count when nothing is filtered; filtered counts are index-assisted.
        var totalTask = hasFilter
            ? collection.CountDocumentsAsync(filter)
            : collection.EstimatedDocumentCountAsync();

        var sort = Builders<LagReport>.Sort.Descending(r => r.CreatedAt);

        // The list view only needs the LagEvents/ConnectionEvents counts, never the
        // heavy per-player MTR/ping arrays. Excluding them avoids deserializing (and
        // then discarding) multi-megabyte diagnostics payloads on every page.
        var projection = Builders<LagReport>.Projection
            .Exclude("Players.Diagnostics.TargetMtr")
            .Exclude("Players.Diagnostics.AllServerBaselines")
            .Exclude("Players.Diagnostics.ReverseMtr")
            .Exclude("Players.Diagnostics.PingHistory")
            .Exclude(r => r.ServerSidePing);

        var itemsTask = collection.Find(filter)
            .Project<LagReport>(projection)
            .Sort(sort)
            .Skip(req.Page * req.PageSize)
            .Limit(req.PageSize)
            .ToListAsync();

        // Run the count and the page fetch concurrently rather than serially.
        await Task.WhenAll(totalTask, itemsTask);

        return (itemsTask.Result, totalTask.Result);
    }

    /// <summary>
    /// Counts grouped by one dimension (see LagReportAggregateDimensions), honoring
    /// the same filters as GetReports. Everything runs inside Mongo as a single
    /// aggregation per dimension (node-day runs two, concurrently) — no per-bucket
    /// queries. The corpus is TTL-bounded to 90 days, so even the unwind-heavy
    /// dimensions stay small.
    /// </summary>
    public async Task<List<LagReportAggregateBucket>> GetAggregate(LagReportAggregateRequest req)
    {
        var collection = CreateCollection<LagReport>();
        var filters = BuildFilters(req);
        var match = filters.Count > 0 ? Builders<LagReport>.Filter.And(filters) : Builders<LagReport>.Filter.Empty;

        return req.GroupBy switch
        {
            LagReportAggregateDimensions.Day => await AggregateByDay(collection, match),
            LagReportAggregateDimensions.NodeDay => await AggregateByNodeDay(collection, match),
            LagReportAggregateDimensions.Category => await AggregateByCategory(collection, match),
            LagReportAggregateDimensions.Server => await AggregateByServer(collection, match),
            LagReportAggregateDimensions.Proxy => await AggregateByProxy(collection, match),
            LagReportAggregateDimensions.BattleTag => await AggregateByBattleTag(collection, match, req.Limit),
            _ => throw new ArgumentException($"Unsupported groupBy '{req.GroupBy}'", nameof(req)),
        };
    }

    // How many category chips a node-day bucket carries; mirrors what the grouped
    // view's headers display.
    private const int NodeDayTopCategories = 4;

    /// <summary>$dateToString runs in UTC when no timezone is given — the same reading
    /// of "day" the date filters use for bare dates.</summary>
    private static BsonDocument DayOfCreatedAt() =>
        new("$dateToString", new BsonDocument { { "format", "%Y-%m-%d" }, { "date", "$CreatedAt" } });

    private static BsonDocument Stage(string name, BsonValue body) => new(name, body);

    /// <summary>
    /// Thin the documents down to the fields a pipeline actually groups on, immediately
    /// after the $match. Storage still reads whole documents — a projection cannot change
    /// that — but the unwind/group stages then carry ~1KB per report instead of the full
    /// ~77KB diagnostics payload.
    /// </summary>
    private static BsonDocument ThinProject(params string[] fields)
    {
        var body = new BsonDocument();
        foreach (var field in fields)
        {
            body[field] = 1;
        }
        return new BsonDocument("$project", body);
    }

    /// <summary>Legacy documents can miss ServerNodeId/ServerNodeName — read them null-safely
    /// so one old document cannot 500 the whole aggregation.</summary>
    private static int IntOrZero(BsonValue value) => value == null || value.IsBsonNull ? 0 : value.ToInt32();

    private static string StringOrEmpty(BsonValue value) => value == null || value.IsBsonNull ? "" : value.AsString;

    private static async Task<List<LagReportAggregateBucket>> AggregateByDay(
        IMongoCollection<LagReport> collection, FilterDefinition<LagReport> match)
    {
        var docs = await collection.Aggregate()
            .Match(match)
            .AppendStage<BsonDocument>(ThinProject("CreatedAt"))
            .AppendStage<BsonDocument>(Stage("$group", new BsonDocument
            {
                { "_id", DayOfCreatedAt() },
                { "count", new BsonDocument("$sum", 1) },
            }))
            .AppendStage<BsonDocument>(Stage("$sort", new BsonDocument("_id", 1)))
            .ToListAsync();

        return docs.Select(d => new LagReportAggregateBucket
        {
            Day = d["_id"].AsString,
            Count = d["count"].ToInt64(),
        }).ToList();
    }

    private static async Task<List<LagReportAggregateBucket>> AggregateByNodeDay(
        IMongoCollection<LagReport> collection, FilterDefinition<LagReport> match)
    {
        // Core buckets: count, submitted count, and distinct players per node × day.
        // "$Players.BattleTag" expands to the array of tags per report; $push makes an
        // array of those arrays, and the $reduce/$setUnion collapses it to a set.
        // Grouped by (day, nodeId) only — a node rename mid-window must not split the
        // bucket, and the categories join below is keyed the same way. The display name
        // rides along as $first.
        var coreTask = collection.Aggregate()
            .Match(match)
            .AppendStage<BsonDocument>(ThinProject("CreatedAt", "ServerNodeId", "ServerNodeName", "HasExplicitReport", "Players.BattleTag"))
            .AppendStage<BsonDocument>(Stage("$group", new BsonDocument
            {
                {
                    "_id",
                    new BsonDocument
                    {
                        { "day", DayOfCreatedAt() },
                        { "nodeId", "$ServerNodeId" },
                    }
                },
                { "nodeName", new BsonDocument("$first", "$ServerNodeName") },
                { "count", new BsonDocument("$sum", 1) },
                { "explicitCount", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray { "$HasExplicitReport", 1, 0 })) },
                { "playerSets", new BsonDocument("$push", "$Players.BattleTag") },
            }))
            .AppendStage<BsonDocument>(Stage("$project", new BsonDocument
            {
                { "nodeName", 1 },
                { "count", 1 },
                { "explicitCount", 1 },
                {
                    "distinctPlayers",
                    new BsonDocument("$size", new BsonDocument("$reduce", new BsonDocument
                    {
                        { "input", "$playerSets" },
                        { "initialValue", new BsonArray() },
                        { "in", new BsonDocument("$setUnion", new BsonArray { "$$value", "$$this" }) },
                    }))
                },
            }))
            .AppendStage<BsonDocument>(Stage("$sort", new BsonDocument { { "_id.day", 1 }, { "count", -1 }, { "_id.nodeId", 1 } }))
            .ToListAsync();

        // Category occurrence counts per node × day, stitched onto the core buckets
        // afterwards — a per-group top-N inside one pipeline needs operators newer
        // than the deployment guarantees, and this second pass is just as cheap.
        var categoriesTask = collection.Aggregate()
            .Match(match)
            .AppendStage<BsonDocument>(ThinProject("CreatedAt", "ServerNodeId", "Players.IssueCategories"))
            .AppendStage<BsonDocument>(Stage("$unwind", "$Players"))
            .AppendStage<BsonDocument>(Stage("$unwind", "$Players.IssueCategories"))
            .AppendStage<BsonDocument>(Stage("$group", new BsonDocument
            {
                {
                    "_id",
                    new BsonDocument
                    {
                        { "day", DayOfCreatedAt() },
                        { "nodeId", "$ServerNodeId" },
                        { "category", "$Players.IssueCategories" },
                    }
                },
                { "count", new BsonDocument("$sum", 1) },
            }))
            .ToListAsync();

        await Task.WhenAll(coreTask, categoriesTask);

        var topCategories = categoriesTask.Result
            .GroupBy(d => (Day: d["_id"]["day"].AsString, NodeId: IntOrZero(d["_id"]["nodeId"])))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(d => d["count"].ToInt64())
                    .ThenBy(d => d["_id"]["category"].ToInt32())
                    .Take(NodeDayTopCategories)
                    .Select(d => new LagReportCategoryCount
                    {
                        Category = CategoryName(d["_id"]["category"]),
                        Count = d["count"].ToInt64(),
                    })
                    .ToList());

        return coreTask.Result.Select(d =>
        {
            var day = d["_id"]["day"].AsString;
            var nodeId = IntOrZero(d["_id"]["nodeId"]);
            return new LagReportAggregateBucket
            {
                Day = day,
                ServerNodeId = nodeId,
                ServerNodeName = StringOrEmpty(d["nodeName"]),
                Count = d["count"].ToInt64(),
                ExplicitCount = d["explicitCount"].ToInt64(),
                DistinctPlayers = d["distinctPlayers"].ToInt32(),
                TopCategories = topCategories.GetValueOrDefault((day, nodeId)) ?? [],
            };
        }).ToList();
    }

    /// <summary>Counts category occurrences per player per report — the same semantics
    /// as the admin UI's facet counts, where two players reporting Desync in one game
    /// count twice.</summary>
    private static async Task<List<LagReportAggregateBucket>> AggregateByCategory(
        IMongoCollection<LagReport> collection, FilterDefinition<LagReport> match)
    {
        var docs = await collection.Aggregate()
            .Match(match)
            .AppendStage<BsonDocument>(ThinProject("Players.IssueCategories"))
            .AppendStage<BsonDocument>(Stage("$unwind", "$Players"))
            .AppendStage<BsonDocument>(Stage("$unwind", "$Players.IssueCategories"))
            .AppendStage<BsonDocument>(Stage("$group", new BsonDocument
            {
                { "_id", "$Players.IssueCategories" },
                { "count", new BsonDocument("$sum", 1) },
            }))
            .AppendStage<BsonDocument>(Stage("$sort", new BsonDocument { { "count", -1 }, { "_id", 1 } }))
            .ToListAsync();

        return docs.Select(d => new LagReportAggregateBucket
        {
            Category = CategoryName(d["_id"]),
            Count = d["count"].ToInt64(),
        }).ToList();
    }

    private static async Task<List<LagReportAggregateBucket>> AggregateByServer(
        IMongoCollection<LagReport> collection, FilterDefinition<LagReport> match)
    {
        var docs = await collection.Aggregate()
            .Match(match)
            .AppendStage<BsonDocument>(ThinProject("ServerNodeId", "ServerNodeName"))
            .AppendStage<BsonDocument>(Stage("$group", new BsonDocument
            {
                { "_id", new BsonDocument { { "nodeId", "$ServerNodeId" }, { "nodeName", "$ServerNodeName" } } },
                { "count", new BsonDocument("$sum", 1) },
            }))
            .AppendStage<BsonDocument>(Stage("$sort", new BsonDocument { { "count", -1 }, { "_id.nodeId", 1 } }))
            .ToListAsync();

        return docs.Select(d => new LagReportAggregateBucket
        {
            ServerNodeId = IntOrZero(d["_id"]["nodeId"]),
            ServerNodeName = StringOrEmpty(d["_id"]["nodeName"]),
            Count = d["count"].ToInt64(),
        }).ToList();
    }

    private static async Task<List<LagReportAggregateBucket>> AggregateByProxy(
        IMongoCollection<LagReport> collection, FilterDefinition<LagReport> match)
    {
        var docs = await collection.Aggregate()
            .Match(match)
            .AppendStage<BsonDocument>(ThinProject("Players.ProxyName"))
            .AppendStage<BsonDocument>(Stage("$unwind", "$Players"))
            .AppendStage<BsonDocument>(Stage("$match", new BsonDocument("Players.ProxyName", new BsonDocument("$ne", BsonNull.Value))))
            .AppendStage<BsonDocument>(Stage("$group", new BsonDocument
            {
                { "_id", "$Players.ProxyName" },
                { "count", new BsonDocument("$sum", 1) },
            }))
            .AppendStage<BsonDocument>(Stage("$sort", new BsonDocument { { "count", -1 }, { "_id", 1 } }))
            .ToListAsync();

        return docs.Select(d => new LagReportAggregateBucket
        {
            ProxyName = d["_id"].AsString,
            Count = d["count"].ToInt64(),
        }).ToList();
    }

    private static async Task<List<LagReportAggregateBucket>> AggregateByBattleTag(
        IMongoCollection<LagReport> collection, FilterDefinition<LagReport> match, int limit)
    {
        var docs = await collection.Aggregate()
            .Match(match)
            .AppendStage<BsonDocument>(ThinProject("Players.BattleTag", "Players.IsExplicit", "ServerNodeId"))
            .AppendStage<BsonDocument>(Stage("$unwind", "$Players"))
            .AppendStage<BsonDocument>(Stage("$group", new BsonDocument
            {
                { "_id", "$Players.BattleTag" },
                { "count", new BsonDocument("$sum", 1) },
                { "submittedCount", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray { "$Players.IsExplicit", 1, 0 })) },
                { "nodes", new BsonDocument("$addToSet", "$ServerNodeId") },
            }))
            .AppendStage<BsonDocument>(Stage("$project", new BsonDocument
            {
                { "count", 1 },
                { "submittedCount", 1 },
                { "distinctNodes", new BsonDocument("$size", "$nodes") },
            }))
            // Submissions first: appearance count tracks activity, not distress, so
            // the ranking (and the Limit cap) must protect actual submitters.
            .AppendStage<BsonDocument>(Stage("$sort", new BsonDocument { { "submittedCount", -1 }, { "count", -1 }, { "_id", 1 } }))
            .AppendStage<BsonDocument>(Stage("$limit", limit))
            .ToListAsync();

        return docs
            .Where(d => !d["_id"].IsBsonNull)
            .Select(d => new LagReportAggregateBucket
            {
                BattleTag = d["_id"].AsString,
                Count = d["count"].ToInt64(),
                SubmittedCount = d["submittedCount"].ToInt64(),
                DistinctNodes = d["distinctNodes"].ToInt32(),
            }).ToList();
    }

    /// <summary>Enums live in BSON as ints (no BsonRepresentation on the model); map
    /// them to names here so the wire format matches the list endpoint's strings.</summary>
    private static string CategoryName(BsonValue value)
    {
        if (value.IsString) return value.AsString;
        var i = value.ToInt32();
        return Enum.IsDefined(typeof(EIssueCategory), i)
            ? ((EIssueCategory)i).ToString()
            : i.ToString(CultureInfo.InvariantCulture);
    }

    private static List<FilterDefinition<LagReport>> BuildFilters(LagReportQueryRequest req)
    {
        var builder = Builders<LagReport>.Filter;
        var filters = new List<FilterDefinition<LagReport>>();

        if (!string.IsNullOrEmpty(req.BattleTag))
        {
            filters.Add(builder.Regex("Players.BattleTagSearch", PrefixPattern(req.BattleTag)));
        }

        if (!string.IsNullOrEmpty(req.GameSearch))
        {
            var gameFilters = new List<FilterDefinition<LagReport>>();

            // Match GameId or FloGameId if the search term is numeric
            if (int.TryParse(req.GameSearch, out var gameIdNum))
            {
                gameFilters.Add(builder.Eq(r => r.GameId, gameIdNum));
                gameFilters.Add(builder.Eq(r => r.FloGameId, gameIdNum));
            }

            // Always also match GameName as a case-insensitive prefix
            gameFilters.Add(builder.Regex(r => r.GameNameSearch, PrefixPattern(req.GameSearch)));

            filters.Add(builder.Or(gameFilters));
        }

        // A report matches when its server name starts with any of the given values.
        // Each value stays a starts-with match, which MongoDB can answer from the
        // index on ServerNodeNameSearch instead of checking every report.
        var serverNames = (req.ServerName ?? []).Where(n => !string.IsNullOrEmpty(n)).ToList();
        if (serverNames.Count > 0)
        {
            filters.Add(builder.Or(serverNames.Select(n =>
                builder.Regex(r => r.ServerNodeNameSearch, PrefixPattern(n)))));
        }

        if (req.ServerNodeId is { Count: > 0 })
        {
            filters.Add(builder.In(r => r.ServerNodeId, req.ServerNodeId));
        }

        if (!string.IsNullOrEmpty(req.ProxyName))
        {
            filters.Add(builder.Regex("Players.ProxyNameSearch", PrefixPattern(req.ProxyName)));
        }

        if (!string.IsNullOrEmpty(req.ProxyIp))
        {
            filters.Add(builder.Regex("Players.ProxyIpSearch", PrefixPattern(req.ProxyIp)));
        }

        if (!string.IsNullOrEmpty(req.DateFrom) && TryParseFilterDate(req.DateFrom, out var dateFrom, out _))
        {
            filters.Add(builder.Gte(r => r.CreatedAt, dateFrom));
        }

        if (!string.IsNullOrEmpty(req.DateTo) && TryParseFilterDate(req.DateTo, out var dateTo, out var toIsBareDate))
        {
            // A bare date names a whole day, so its upper bound is the start of the next one.
            // Taken literally it is midnight, which excludes every report of the day asked for.
            filters.Add(toIsBareDate
                ? builder.Lt(r => r.CreatedAt, dateTo.AddDays(1))
                : builder.Lte(r => r.CreatedAt, dateTo));
        }

        // A report matches when any of its players reports any of the given categories.
        // Values that don't name a known category are skipped, as before. The string
        // field path makes this a plain "value in list" query that the index on
        // Players.IssueCategories answers directly.
        var categories = (req.IssueCategory ?? [])
            .Select(c => Enum.TryParse<EIssueCategory>(c, out var parsed) ? parsed : (EIssueCategory?)null)
            .Where(c => c.HasValue)
            .Select(c => c.Value)
            .ToList();
        if (categories.Count > 0)
        {
            filters.Add(builder.AnyIn("Players.IssueCategories", categories));
        }

        // ignoreCase: true — ELagReportTag has mixed-case members (LAN, LastMile); URL query params
        // shouldn't need exact casing (matches the wire converter's case-insensitive read).
        var tags = (req.ConnectionIssueTag ?? [])
            .Select(t => Enum.TryParse<ELagReportTag>(t, ignoreCase: true, out var parsed) ? parsed : (ELagReportTag?)null)
            .Where(t => t.HasValue)
            .Select(t => t.Value)
            .ToList();
        if (tags.Count > 0)
        {
            filters.Add(builder.AnyIn("Players.ConnectionIssueTags", tags));
        }

        if (req.ExplicitOnly == true)
        {
            filters.Add(builder.Eq(r => r.HasExplicitReport, true));
        }

        if (req.MinPlayers is > 0)
        {
            filters.Add(builder.Gte(r => r.PlayerCount, req.MinPlayers.Value));
        }

        if (req.MaxPlayers is > 0)
        {
            filters.Add(builder.Lte(r => r.PlayerCount, req.MaxPlayers.Value));
        }

        return filters;
    }

    /// <summary>
    /// Parses a date filter to UTC, reporting whether it named a bare day (yyyy-MM-dd, what
    /// an &lt;input type="date"&gt; sends) or an instant. The result must be a DateTime because
    /// CreatedAt is one: comparing it against a DateTimeOffset compiles via the implicit
    /// conversion, but leaves the field expression a Convert node the driver cannot translate,
    /// so every date-filtered query throws instead of running.
    /// AssumeUniversal fixes a bare date to the same window whatever the server's timezone;
    /// a value carrying its own offset keeps it.
    /// </summary>
    private static bool TryParseFilterDate(string value, out DateTime parsed, out bool isBareDate)
    {
        const DateTimeStyles styles = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;

        isBareDate = DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, styles, out parsed);

        return isBareDate || DateTime.TryParse(value, CultureInfo.InvariantCulture, styles, out parsed);
    }

    /// <summary>
    /// Anchored, case-sensitive regex against a lowercased *Search field — index-backed
    /// case-insensitive PREFIX matching. A case-insensitive regex on the raw field cannot
    /// use the index and scans the whole collection.
    /// </summary>
    private static BsonRegularExpression PrefixPattern(string value) =>
        new("^" + Regex.Escape(value.ToLowerInvariant()));

    /// <summary>
    /// One-shot backfill of the lowercased *Search fields onto documents written before the
    /// fields existed. Idempotent via the {GameNameSearch exists:false} guard; runs entirely
    /// server-side as a single aggregation-pipeline UpdateMany. Returns documents updated.
    /// </summary>
    public async Task<long> BackfillSearchFields(CancellationToken ct = default)
    {
        var collection = CreateCollection<LagReport>();

        var filter = Builders<LagReport>.Filter.Exists(r => r.GameNameSearch, false);

        // $toLower yields "" for null/missing; keep null instead so backfilled values match the
        // write path (BattleTag?.ToLowerInvariant()) and direct-connection players stay null.
        static BsonValue Lower(string field) => new BsonDocument("$cond", new BsonArray
        {
            new BsonDocument("$ne", new BsonArray { field, BsonNull.Value }),
            new BsonDocument("$toLower", field),
            BsonNull.Value,
        });

        var setStage = new BsonDocument("$set", new BsonDocument
        {
            { "GameNameSearch", Lower("$GameName") },
            { "ServerNodeNameSearch", Lower("$ServerNodeName") },
            {
                "Players",
                new BsonDocument("$map", new BsonDocument
                {
                    { "input", "$Players" },
                    { "as", "p" },
                    {
                        "in",
                        new BsonDocument("$mergeObjects", new BsonArray
                        {
                            "$$p",
                            new BsonDocument
                            {
                                { "BattleTagSearch", Lower("$$p.BattleTag") },
                                { "ProxyNameSearch", Lower("$$p.ProxyName") },
                                { "ProxyIpSearch", Lower("$$p.ProxyIp") },
                            },
                        })
                    },
                })
            },
        });

        var pipeline = new BsonDocumentStagePipelineDefinition<LagReport, LagReport>(new[] { setStage });
        var result = await collection.UpdateManyAsync(filter, pipeline, cancellationToken: ct);
        return result.ModifiedCount;
    }

    /// <summary>
    /// One-shot backfill of PlayerCount onto documents written before the field existed —
    /// BackfillSearchFields' pattern: idempotent via the exists guard, one server-side
    /// pipeline UpdateMany. Returns documents updated.
    /// </summary>
    public async Task<long> BackfillPlayerCounts(CancellationToken ct = default)
    {
        var collection = CreateCollection<LagReport>();

        var filter = Builders<LagReport>.Filter.Exists(r => r.PlayerCount, false);

        var setStage = new BsonDocument("$set", new BsonDocument("PlayerCount",
            new BsonDocument("$size", new BsonDocument("$ifNull", new BsonArray { "$Players", new BsonArray() }))));

        var pipeline = new BsonDocumentStagePipelineDefinition<LagReport, LagReport>(new[] { setStage });
        var result = await collection.UpdateManyAsync(filter, pipeline, cancellationToken: ct);
        return result.ModifiedCount;
    }
}
