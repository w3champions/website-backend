using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace W3ChampionsStatisticService.Tools.MapKeyBackfill;

public sealed class MapKeyBackfillRunner(MapKeyBackfillOptions options, MapKeyBackfillTarget target, Action<string> log)
{
    private static readonly BulkWriteOptions Unordered = new() { IsOrdered = false };

    private readonly MapKeyBackfillOptions _options = options;
    private readonly MapKeyBackfillTarget _target = target;
    private readonly Action<string> _log = log;

    public MapKeyReport Report { get; } = new();

    public async Task RunAsync()
    {
        var settings = MongoClientSettings.FromConnectionString(_options.ConnectionString.Replace("'", string.Empty));
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);
        settings.ConnectTimeout = TimeSpan.FromSeconds(10);
        var database = new MongoClient(settings).GetDatabase(_options.DatabaseName);
        var events = database.GetCollection<BsonDocument>(EventMapCatalog.EventCollectionName);

        var catalog = await LoadCatalogAsync(events);
        var mapData = ReferenceCatalogs.LoadMapData(_options.UpdateServiceMapDataPath, m => _log($"Warning: {m}"));
        var mmMaps = ReferenceCatalogs.LoadMmMaps(_options.MmMapsPath);
        var manualMap = ManualMap.Load(_options.ManualMapPath);
        var resolver = new MapKeyResolver(catalog.Groups, mapData, mmMaps, manualMap);

        _log($"MapData files: {mapData.Count}, MM Maps names: {mmMaps.Count}, manual map entries: {manualMap.Entries.Count}");
        _log($"Seasons with events: {string.Join(", ", resolver.EventSeasons.OrderBy(s => s))}");

        foreach (var collection in _options.Collections)
        {
            _log($"Processing {collection}...");
            var mongo = database.GetCollection<BsonDocument>(collection);
            switch (collection)
            {
                case MapKeyTransforms.Matchup:
                    await ProcessMatchupAsync(mongo, events, resolver);
                    break;
                case MapKeyTransforms.MapsPerSeason:
                    await ProcessMapsPerSeasonAsync(mongo, resolver);
                    break;
                case MapKeyTransforms.PlayerHeroStats:
                    await ProcessPagedStatsAsync(mongo, collection, doc => MapKeyTransforms.TransformPlayerHeroStats(doc, resolver, Report));
                    break;
                case MapKeyTransforms.PlayerRaceOnMapVersusRaceRatio:
                    await ProcessPagedStatsAsync(mongo, collection, doc => MapKeyTransforms.TransformPlayerRaceOnMapVersusRaceRatio(doc, resolver, Report));
                    break;
                case MapKeyTransforms.OverallRaceAndWinStat:
                    await ProcessWholeStatsAsync(mongo, collection, doc => MapKeyTransforms.TransformOverallRaceAndWinStat(doc, resolver, Report));
                    break;
            }
        }

        Report.Write(_options.ReportPath, id => resolver.MmName(id));
        PrintSummary();
    }

    private async Task<EventMapCatalog> LoadCatalogAsync(IMongoCollection<BsonDocument> events)
    {
        EventMapCatalog catalog;
        if (_options.EventCatalogPath != null)
        {
            catalog = EventMapCatalog.Load(_options.EventCatalogPath);
            _log($"Event catalog read from {_options.EventCatalogPath}.");
        }
        else
        {
            _log($"Aggregating {EventMapCatalog.EventCollectionName} month by month...");
            catalog = await EventMapCatalog.AggregateAsync(events, _options.DatabaseName, _target.Servers, TimeSpan.FromMilliseconds(_options.AggregationPauseMs), _log);
            if (_options.WriteEventCatalogPath != null)
            {
                catalog.Save(_options.WriteEventCatalogPath);
                _log($"Event catalog written to {_options.WriteEventCatalogPath}.");
            }
        }

        var header = catalog.Header;
        _log($"Event catalog: database {header.Database}, host(s) {string.Join(", ", header.Hosts)}, created {header.CreatedAt:u}, {header.Events} events in {header.Groups} groups.");
        if (_options.EventCatalogPath != null && header.Database != _options.DatabaseName)
        {
            _log($"Warning: the event catalog was built from database '{header.Database}', not '{_options.DatabaseName}'.");
        }

        return catalog;
    }

    private async Task ProcessMatchupAsync(IMongoCollection<BsonDocument> matchups, IMongoCollection<BsonDocument> events, MapKeyResolver resolver)
    {
        var totals = Report.TotalsFor(MapKeyTransforms.Matchup);
        var projection = Builders<BsonDocument>.Projection.Include("Map").Include("MapId").Include("MapName").Include("Season").Include("GameMode").Include("MatchId");
        BsonValue lastId = _options.StartAfter != null ? ObjectId.Parse(_options.StartAfter) : null;

        while (true)
        {
            var page = await NextPageAsync(matchups, lastId, projection);
            if (page.Count == 0)
            {
                break;
            }

            lastId = page[^1]["_id"];
            totals.DocumentsRead += page.Count;

            var joined = await JoinEventsAsync(page, events, resolver);
            var writes = new List<WriteModel<BsonDocument>>();
            foreach (var doc in page)
            {
                joined.TryGetValue(doc["_id"], out var evt);
                var set = MapKeyTransforms.MatchupUpdate(doc, evt, resolver, Report);
                if (set == null)
                {
                    continue;
                }

                var filter = Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]) & Builders<BsonDocument>.Filter.Eq("Map", doc["Map"]);
                writes.Add(new UpdateOneModel<BsonDocument>(filter, new BsonDocument("$set", set)));
            }

            await WriteAsync(matchups, writes, totals, lastId);
        }
    }

    // Joins the Matchup documents that still hold a legacy key, in seasons with events, to their event: by _id, then
    // by MatchId <-> match._id.
    private static async Task<Dictionary<BsonValue, JoinedEventMap>> JoinEventsAsync(
        IReadOnlyList<BsonDocument> page,
        IMongoCollection<BsonDocument> events,
        MapKeyResolver resolver)
    {
        var result = new Dictionary<BsonValue, JoinedEventMap>();
        var pending = page
            .Where(d => d.GetValue("Map", BsonNull.Value) is BsonString map
                && !resolver.IsKnownFile(map.Value)
                && d.GetValue("Season", BsonNull.Value) is { IsNumeric: true } season
                && resolver.HasEvents(season.ToInt32()))
            .ToList();
        if (pending.Count == 0)
        {
            return result;
        }

        var projection = Builders<BsonDocument>.Projection.Include("match.map").Include("match.mapId").Include("match.mapName").Include("match._id");
        var byId = await events.Find(Builders<BsonDocument>.Filter.In("_id", pending.Select(d => d["_id"]))).Project(projection).ToListAsync();
        foreach (var evt in byId)
        {
            result[evt["_id"]] = ReadEventMap(evt);
        }

        // Every Matchup with that MatchId gets the event, also when several share it.
        var missing = pending
            .Where(d => !result.ContainsKey(d["_id"]) && d.GetValue("MatchId", BsonNull.Value).IsString)
            .GroupBy(d => d["MatchId"].AsString, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(d => d["_id"]).ToList(), StringComparer.Ordinal);
        if (missing.Count > 0)
        {
            var byMatchId = await events.Find(Builders<BsonDocument>.Filter.In("match._id", missing.Keys)).Project(projection).ToListAsync();
            foreach (var evt in byMatchId)
            {
                if (evt.GetValue("match", BsonNull.Value) is BsonDocument match
                    && match.GetValue("_id", BsonNull.Value) is BsonString matchId
                    && missing.TryGetValue(matchId.Value, out var matchupIds))
                {
                    var eventMap = ReadEventMap(evt);
                    foreach (var matchupId in matchupIds)
                    {
                        result[matchupId] = eventMap;
                    }
                }
            }
        }

        return result;
    }

    private static JoinedEventMap ReadEventMap(BsonDocument evt)
    {
        var match = evt.GetValue("match", BsonNull.Value) as BsonDocument ?? new BsonDocument();
        var mapId = match.GetValue("mapId", BsonNull.Value);
        return new JoinedEventMap(
            match.GetValue("map", BsonNull.Value) is BsonString path ? path.Value : null,
            mapId.IsNumeric ? mapId.ToInt32() : null,
            match.GetValue("mapName", BsonNull.Value) is BsonString name ? name.Value : null);
    }

    private async Task ProcessPagedStatsAsync(IMongoCollection<BsonDocument> collection, string name, Func<BsonDocument, MapKeyTransformResult> transform)
    {
        var totals = Report.TotalsFor(name);
        BsonValue lastId = _options.StartAfter != null ? new BsonString(_options.StartAfter) : null;

        while (true)
        {
            var page = await NextPageAsync(collection, lastId, null);
            if (page.Count == 0)
            {
                break;
            }

            lastId = page[^1]["_id"];
            totals.DocumentsRead += page.Count;
            var writes = page
                .Select(doc => (Stored: doc, Result: transform(doc)))
                .Where(r => r.Result.Changed)
                .Select(r => (WriteModel<BsonDocument>)GuardedReplace(r.Stored, r.Result.Document))
                .ToList();

            await WriteAsync(collection, writes, totals, lastId);
        }
    }

    private async Task ProcessWholeStatsAsync(IMongoCollection<BsonDocument> collection, string name, Func<BsonDocument, MapKeyTransformResult> transform)
    {
        var totals = Report.TotalsFor(name);
        var documents = await collection.Find(FilterDefinition<BsonDocument>.Empty).Sort(Builders<BsonDocument>.Sort.Ascending("_id")).ToListAsync();
        totals.DocumentsRead += documents.Count;
        var writes = documents
            .Select(doc => (Stored: doc, Result: transform(doc)))
            .Where(r => r.Result.Changed)
            .Select(r => (WriteModel<BsonDocument>)GuardedReplace(r.Stored, r.Result.Document))
            .ToList();

        await WriteAsync(collection, writes, totals, null);
    }

    // Season documents first; the all-seasons document is rebuilt from their migrated form.
    private async Task ProcessMapsPerSeasonAsync(IMongoCollection<BsonDocument> collection, MapKeyResolver resolver)
    {
        var totals = Report.TotalsFor(MapKeyTransforms.MapsPerSeason);
        var documents = await collection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        totals.DocumentsRead += documents.Count;

        var seasons = documents
            .Where(d => !IsAllSeasons(d))
            .OrderBy(d => MapKeyTransforms.SeasonOf(d) ?? int.MaxValue)
            .Select(d => MapKeyTransforms.TransformMapsPerSeasonSeason(d, resolver, Report))
            .ToList();

        var results = new List<MapKeyTransformResult>(seasons);
        var allSeasons = documents.FirstOrDefault(IsAllSeasons);
        if (allSeasons != null)
        {
            var rebuilt = MapKeyTransforms.RebuildMapsPerSeasonAllSeasons(allSeasons, seasons.Select(s => s.Document).ToList(), resolver, Report);
            results.Add(rebuilt);
            foreach (var row in Report.Rows.Where(r => r.Collection == MapKeyTransforms.MapsPerSeason && r.TotalsDiffer))
            {
                _log($"Warning: MapsPerSeason season {row.Season}, mode {GameModeNames.NameOf(row.GameMode)}: {row.Notes}");
            }
        }

        var stored = documents.ToDictionary(d => d["_id"]);
        var writes = results
            .Where(r => r.Changed)
            .Select(r => (WriteModel<BsonDocument>)GuardedReplace(stored[r.Document["_id"]], r.Document))
            .ToList();

        await WriteAsync(collection, writes, totals, null);
    }

    // Replaces a stats document only while every field the backfill rewrites still holds the value that was read. A
    // document a handler saved in between is not matched, so its new games are not overwritten; it is counted as
    // skipped and a re-run migrates it.
    public static ReplaceOneModel<BsonDocument> GuardedReplace(BsonDocument stored, BsonDocument migrated)
    {
        var builder = Builders<BsonDocument>.Filter;
        var filter = builder.Eq("_id", stored["_id"]);
        foreach (var element in migrated.Where(e => e.Name != "_id"))
        {
            if (!stored.TryGetValue(element.Name, out var storedValue))
            {
                filter &= builder.Exists(element.Name, false);
            }
            else if (!storedValue.Equals(element.Value))
            {
                filter &= builder.Eq(element.Name, storedValue);
            }
        }

        foreach (var element in stored.Where(e => !migrated.Contains(e.Name)))
        {
            filter &= builder.Eq(element.Name, element.Value);
        }

        return new ReplaceOneModel<BsonDocument>(filter, migrated);
    }

    private static bool IsAllSeasons(BsonDocument document)
    {
        return document.GetValue("_id", BsonNull.Value) is BsonString id && id.Value == MapKeyTransforms.AllSeasonsId;
    }

    private async Task<List<BsonDocument>> NextPageAsync(IMongoCollection<BsonDocument> collection, BsonValue lastId, ProjectionDefinition<BsonDocument> projection)
    {
        var filter = lastId == null ? FilterDefinition<BsonDocument>.Empty : Builders<BsonDocument>.Filter.Gt("_id", lastId);
        var find = collection.Find(filter).Sort(Builders<BsonDocument>.Sort.Ascending("_id")).Limit(_options.BatchSize);
        return projection == null ? await find.ToListAsync() : await find.Project(projection).ToListAsync();
    }

    private async Task WriteAsync(IMongoCollection<BsonDocument> collection, IReadOnlyList<WriteModel<BsonDocument>> writes, MapKeyCollectionTotals totals, BsonValue lastId)
    {
        totals.DocumentsChanged += writes.Count;
        if (_options.Apply && writes.Count > 0)
        {
            var result = await collection.BulkWriteAsync(writes, Unordered);
            totals.DocumentsWritten += result.ModifiedCount;
            totals.DocumentsSkipped += writes.Count - result.MatchedCount;
            if (_options.BatchPauseMs > 0)
            {
                await Task.Delay(_options.BatchPauseMs);
            }
        }

        if (lastId != null && totals.DocumentsRead % (_options.BatchSize * 50L) < _options.BatchSize)
        {
            _log($"  {collection.CollectionNamespace.CollectionName}: {totals.DocumentsRead} read, {totals.DocumentsChanged} to change, last _id {lastId}");
        }
    }

    private void PrintSummary()
    {
        _log($"Report written to {_options.ReportPath}");
        foreach (var collection in _options.Collections)
        {
            var totals = Report.TotalsFor(collection);
            var written = _options.Apply ? $", {totals.DocumentsWritten} written" : " (dry run, nothing written)";
            _log($"{collection}: {totals.DocumentsRead} documents read, {totals.DocumentsChanged} to change{written}; {totals.EntriesAlreadyMigrated} entries already migrated.");
            if (totals.DocumentsSkipped > 0)
            {
                _log($"Warning: {collection}: {totals.DocumentsSkipped} documents changed after they were read and were left as they are. Run again (with the handlers stopped) to migrate them.");
            }

            foreach (var status in Report.Rows.Where(r => r.Collection == collection).GroupBy(r => r.Status).OrderBy(g => g.Key))
            {
                _log($"  {status.Key.ToCsv()}: {status.Count()} keys, {status.Sum(r => r.Entries)} entries, {status.Sum(r => r.EntriesMerged)} merged");
            }
        }
    }
}
