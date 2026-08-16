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
