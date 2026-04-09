using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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

            // Server name filter (partial match)
            new(Builders<LagReport>.IndexKeys.Ascending(r => r.ServerNodeName)),

            // Game name filter (partial match)
            new(Builders<LagReport>.IndexKeys.Ascending(r => r.GameName)),

            // Player battle tag filter (inside nested array)
            new(Builders<LagReport>.IndexKeys.Ascending("Players.BattleTag")),

            // Proxy name filter (inside nested array)
            new(Builders<LagReport>.IndexKeys.Ascending("Players.ProxyName")),

            // Proxy IP filter (inside nested array)
            new(Builders<LagReport>.IndexKeys.Ascending("Players.ProxyIp")),

            // Issue category filter (inside nested array)
            new(Builders<LagReport>.IndexKeys.Ascending("Players.IssueCategories")),

            // Explicit filter — most reports are auto-submitted, admins typically filter to explicit only
            new(Builders<LagReport>.IndexKeys.Ascending(r => r.HasExplicitReport)),

            // Default list sort + date range filter
            new(Builders<LagReport>.IndexKeys.Descending(r => r.CreatedAt)),

            // TTL: auto-expire documents 90 days after last update
            new(Builders<LagReport>.IndexKeys.Ascending(r => r.UpdatedAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(90) }),
        };

        await collection.Indexes.CreateManyAsync(indexes);
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

        // Atomic upsert: push the player and set UpdatedAt on every call,
        // $setOnInsert the template fields only when creating a new document.
        var update = Builders<LagReport>.Update
            .Push(r => r.Players, playerData)
            .Set(r => r.UpdatedAt, DateTime.UtcNow)
            .SetOnInsert(r => r.Id, template.Id)
            .SetOnInsert(r => r.GameId, template.GameId)
            .SetOnInsert(r => r.FloGameId, template.FloGameId)
            .SetOnInsert(r => r.GameName, template.GameName)
            .SetOnInsert(r => r.MapPath, template.MapPath)
            .SetOnInsert(r => r.ServerNodeId, template.ServerNodeId)
            .SetOnInsert(r => r.ServerNodeName, template.ServerNodeName)
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
        var filter = BuildFilter(req);

        var total = await collection.CountDocumentsAsync(filter);

        var sort = Builders<LagReport>.Sort.Descending(r => r.CreatedAt);

        var items = await collection.Find(filter)
            .Sort(sort)
            .Skip(req.Page * req.PageSize)
            .Limit(req.PageSize)
            .ToListAsync();

        return (items, total);
    }

    private static FilterDefinition<LagReport> BuildFilter(LagReportQueryRequest req)
    {
        var builder = Builders<LagReport>.Filter;
        var filters = new List<FilterDefinition<LagReport>>();

        if (!string.IsNullOrEmpty(req.BattleTag))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(req.BattleTag), "i");
            filters.Add(builder.ElemMatch(r => r.Players,
                Builders<LagReportPlayer>.Filter.Regex(p => p.BattleTag, pattern)));
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

            // Always also match GameName as partial (case-insensitive)
            var namePattern = new BsonRegularExpression(Regex.Escape(req.GameSearch), "i");
            gameFilters.Add(builder.Regex(r => r.GameName, namePattern));

            filters.Add(builder.Or(gameFilters));
        }

        if (!string.IsNullOrEmpty(req.ServerName))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(req.ServerName), "i");
            filters.Add(builder.Regex(r => r.ServerNodeName, pattern));
        }

        if (!string.IsNullOrEmpty(req.ProxyName))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(req.ProxyName), "i");
            filters.Add(builder.ElemMatch(r => r.Players,
                Builders<LagReportPlayer>.Filter.Regex(p => p.ProxyName, pattern)));
        }

        if (!string.IsNullOrEmpty(req.ProxyIp))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(req.ProxyIp), "i");
            filters.Add(builder.ElemMatch(r => r.Players,
                Builders<LagReportPlayer>.Filter.Regex(p => p.ProxyIp, pattern)));
        }

        if (!string.IsNullOrEmpty(req.DateFrom) && DateTimeOffset.TryParse(req.DateFrom, out var dateFrom))
        {
            filters.Add(builder.Gte(r => r.CreatedAt, dateFrom));
        }

        if (!string.IsNullOrEmpty(req.DateTo) && DateTimeOffset.TryParse(req.DateTo, out var dateTo))
        {
            filters.Add(builder.Lte(r => r.CreatedAt, dateTo));
        }

        if (!string.IsNullOrEmpty(req.IssueCategory) && Enum.TryParse<EIssueCategory>(req.IssueCategory, out var category))
        {
            filters.Add(builder.ElemMatch(r => r.Players, p => p.IssueCategories.Contains(category)));
        }

        if (req.ExplicitOnly == true)
        {
            filters.Add(builder.Eq(r => r.HasExplicitReport, true));
        }

        return filters.Count > 0 ? builder.And(filters) : builder.Empty;
    }
}
