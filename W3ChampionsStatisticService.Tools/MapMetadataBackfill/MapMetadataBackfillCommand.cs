using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace W3ChampionsStatisticService.Tools.MapMetadataBackfill;

public static class MapMetadataBackfillCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        MapMetadataBackfillOptions options;
        try
        {
            options = MapMetadataBackfillOptions.Parse(args);
        }
        catch (HelpRequestedException)
        {
            Console.WriteLine(MapMetadataBackfillOptions.Usage);
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(MapMetadataBackfillOptions.Usage);
            return 2;
        }

        try
        {
            await RunBackfillAsync(options);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task RunBackfillAsync(MapMetadataBackfillOptions options)
    {
        Console.WriteLine(options.Apply ? "Running map metadata backfill in APPLY mode." : "Running map metadata backfill in dry-run mode.");
        Console.WriteLine($"Database: {options.DatabaseName}, collection: {options.CollectionName}");
        Console.WriteLine($"Target seasons: {options.TargetMinSeason}-{options.TargetMaxSeason}; source seasons: {options.SourceMinSeason}-{(options.SourceMaxSeason?.ToString() ?? "latest")}");
        Console.WriteLine(options.HasGameModeFilter
            ? $"Game modes: {string.Join(", ", options.GameModes.OrderBy(g => g))}"
            : "Game modes: all");

        var mongoSettings = MongoClientSettings.FromConnectionString(options.ConnectionString.Replace("'", string.Empty));
        mongoSettings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);
        mongoSettings.ConnectTimeout = TimeSpan.FromSeconds(10);

        var client = new MongoClient(mongoSettings);
        var collection = client.GetDatabase(options.DatabaseName).GetCollection<BsonDocument>(options.CollectionName);

        var sourceMetadata = await LoadSourceMetadataAsync(collection, options);
        var targetGroups = await LoadTargetGroupsAsync(collection, options);
        var manualMetadata = MapMetadataResolver.LoadManualMetadata(options.ManualMapPath);
        var resolver = new MapMetadataResolver(sourceMetadata, manualMetadata);

        Console.WriteLine($"Loaded {sourceMetadata.Count} source map metadata rows.");
        Console.WriteLine($"Found {targetGroups.Count} target map groups with missing metadata.");

        var reportRows = new List<MapMetadataBackfillReportRow>();
        foreach (var target in targetGroups.OrderBy(t => t.Map, StringComparer.OrdinalIgnoreCase))
        {
            var resolution = resolver.Resolve(target.Map);
            var action = GetAction(resolution, options, target);
            long modifiedCount = 0;

            if (options.Apply && action == "update")
            {
                modifiedCount = await ApplyMapMetadataAsync(collection, options, target, resolution);
            }

            reportRows.Add(new MapMetadataBackfillReportRow(
                target.Map,
                target.Count,
                target.Seasons,
                target.GameModes,
                resolution.MapName,
                resolution.MapId,
                resolution.Confidence,
                resolution.Status.ToString(),
                options.Apply ? action : $"dry-run:{action}",
                modifiedCount,
                target.SampleIds,
                resolution.SourceMaps,
                AppendActionNote(resolution, options, target, action)));
        }

        await CsvReportWriter.WriteAsync(options.ReportPath, reportRows);

        var readyCount = reportRows.Count(r => r.Action.EndsWith(":update", StringComparison.OrdinalIgnoreCase) || r.Action == "update");
        var ambiguousCount = reportRows.Count(r => r.Status == MapMetadataResolutionStatus.Ambiguous.ToString());
        var missingCount = reportRows.Count(r => r.Status == MapMetadataResolutionStatus.Missing.ToString());
        var skippedNoMapIdCount = reportRows.Count(r => r.Action.EndsWith(":skip-no-map-id", StringComparison.OrdinalIgnoreCase) || r.Action == "skip-no-map-id");
        var modifiedTotal = reportRows.Sum(r => r.ModifiedCount);

        Console.WriteLine($"Report written to {options.ReportPath}");
        Console.WriteLine($"Ready: {readyCount}, ambiguous: {ambiguousCount}, missing: {missingCount}, skipped without map id: {skippedNoMapIdCount}");
        PrintPreview(reportRows, options);
        if (options.Apply)
        {
            Console.WriteLine($"Modified documents: {modifiedTotal}");
        }
    }

    private static void PrintPreview(IReadOnlyCollection<MapMetadataBackfillReportRow> reportRows, MapMetadataBackfillOptions options)
    {
        if (options.PreviewLimit == 0 || reportRows.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine(options.Apply ? "Applied changes:" : "Dry-run preview:");

        foreach (var row in reportRows.Take(options.PreviewLimit))
        {
            var seasonText = RangeLabel(row.Seasons);
            var modeText = row.GameModes.Count > 0 ? $" modes {string.Join("/", row.GameModes.OrderBy(g => g))}" : string.Empty;
            var mapIdText = row.ProposedMapId.HasValue ? $" (id {row.ProposedMapId.Value})" : string.Empty;

            if (row.Action.EndsWith(":update", StringComparison.OrdinalIgnoreCase) || row.Action == "update")
            {
                var verb = options.Apply ? "updated" : "would update";
                Console.WriteLine($"  {verb} {row.TargetMap} -> {row.ProposedMapName}{mapIdText}: {row.MatchCount} matches, seasons {seasonText}{modeText}");
                continue;
            }

            var reason = row.Status.Equals(MapMetadataResolutionStatus.Ambiguous.ToString(), StringComparison.OrdinalIgnoreCase)
                ? "ambiguous"
                : row.Status.Equals(MapMetadataResolutionStatus.Missing.ToString(), StringComparison.OrdinalIgnoreCase)
                    ? "missing mapping"
                    : row.Action.Replace("dry-run:", string.Empty);

            Console.WriteLine($"  skip {row.TargetMap}: {reason}, {row.MatchCount} matches, seasons {seasonText}{modeText}");
        }

        if (reportRows.Count > options.PreviewLimit)
        {
            Console.WriteLine($"  ... {reportRows.Count - options.PreviewLimit} more rows in the CSV report");
        }
    }

    private static string RangeLabel(IReadOnlyCollection<int> values)
    {
        if (values.Count == 0)
        {
            return "unknown";
        }

        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 1 ? sorted[0].ToString() : $"{sorted.First()}-{sorted.Last()}";
    }

    private static string GetAction(
        MapMetadataResolution resolution,
        MapMetadataBackfillOptions options,
        TargetMapGroup target)
    {
        if (!resolution.CanApply)
        {
            return resolution.Status == MapMetadataResolutionStatus.Ambiguous ? "skip-ambiguous" : "skip-missing";
        }

        if (options.RequireMapId && !resolution.MapId.HasValue)
        {
            return "skip-no-map-id";
        }

        if (target.MissingMapNameCount > 0)
        {
            return "update";
        }

        if (target.MissingMapIdCount > 0 && resolution.MapId.HasValue)
        {
            return "update";
        }

        if (target.MissingMapIdCount > 0)
        {
            return "skip-no-map-id";
        }

        return "skip-no-change";
    }

    private static string AppendRequireMapIdNote(MapMetadataResolution resolution, MapMetadataBackfillOptions options)
    {
        if (options.RequireMapId && resolution.CanApply && !resolution.MapId.HasValue)
        {
            return $"{resolution.Notes} Skipped because --require-map-id is set.";
        }

        return resolution.Notes;
    }

    private static string AppendActionNote(
        MapMetadataResolution resolution,
        MapMetadataBackfillOptions options,
        TargetMapGroup target,
        string action)
    {
        var notes = AppendRequireMapIdNote(resolution, options);
        if (action == "skip-no-map-id" && target.MissingMapNameCount == 0)
        {
            return $"{notes} MapName is already set; MapId is still unknown.";
        }

        if (action == "skip-no-change")
        {
            return $"{notes} Nothing to update.";
        }

        return notes;
    }

    private static async Task<IReadOnlyCollection<SourceMapMetadata>> LoadSourceMetadataAsync(
        IMongoCollection<BsonDocument> collection,
        MapMetadataBackfillOptions options)
    {
        var filter = BuildSeasonFilter(options.SourceMinSeason, options.SourceMaxSeason, options)
            & Builders<BsonDocument>.Filter.Exists("Map", true)
            & Builders<BsonDocument>.Filter.Ne("Map", BsonNull.Value)
            & Builders<BsonDocument>.Filter.Ne("Map", string.Empty)
            & Builders<BsonDocument>.Filter.Exists("MapName", true)
            & Builders<BsonDocument>.Filter.Ne("MapName", BsonNull.Value)
            & Builders<BsonDocument>.Filter.Ne("MapName", string.Empty);

        var group = new BsonDocument
        {
            ["_id"] = new BsonDocument
            {
                ["Map"] = "$Map",
                ["MapName"] = "$MapName",
                ["MapId"] = "$MapId"
            },
            ["Count"] = new BsonDocument("$sum", 1),
            ["MinSeason"] = new BsonDocument("$min", "$Season"),
            ["MaxSeason"] = new BsonDocument("$max", "$Season"),
            ["GameModes"] = new BsonDocument("$addToSet", "$GameMode")
        };

        var docs = await collection.Aggregate()
            .Match(filter)
            .Group(group)
            .ToListAsync();

        return docs
            .Select(doc =>
            {
                var id = doc["_id"].AsBsonDocument;
                return new SourceMapMetadata(
                    id.GetValue("Map").AsString,
                    id.GetValue("MapName").AsString,
                    ReadNullableInt(id, "MapId"),
                    ReadInt(doc, "MinSeason"),
                    ReadInt(doc, "MaxSeason"),
                    ReadIntArray(doc, "GameModes"),
                    ReadLong(doc, "Count"));
            })
            .ToList();
    }

    private static async Task<IReadOnlyCollection<TargetMapGroup>> LoadTargetGroupsAsync(
        IMongoCollection<BsonDocument> collection,
        MapMetadataBackfillOptions options)
    {
        var filter = BuildSeasonFilter(options.TargetMinSeason, options.TargetMaxSeason, options)
            & Builders<BsonDocument>.Filter.Exists("Map", true)
            & Builders<BsonDocument>.Filter.Ne("Map", BsonNull.Value)
            & Builders<BsonDocument>.Filter.Ne("Map", string.Empty)
            & Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Exists("MapName", false),
                Builders<BsonDocument>.Filter.Eq("MapName", BsonNull.Value),
                Builders<BsonDocument>.Filter.Eq("MapName", string.Empty),
                Builders<BsonDocument>.Filter.Exists("MapId", false),
                Builders<BsonDocument>.Filter.Eq("MapId", BsonNull.Value));

        var group = new BsonDocument
        {
            ["_id"] = "$Map",
            ["Count"] = new BsonDocument("$sum", 1),
            ["MissingMapNameCount"] = CountMissingOrEmptyExpression("$MapName"),
            ["MissingMapIdCount"] = CountMissingExpression("$MapId"),
            ["Seasons"] = new BsonDocument("$addToSet", "$Season"),
            ["GameModes"] = new BsonDocument("$addToSet", "$GameMode"),
            ["SampleIds"] = new BsonDocument("$addToSet", "$_id")
        };

        var docs = await collection.Aggregate()
            .Match(filter)
            .Group(group)
            .ToListAsync();

        return docs
            .Select(doc => new TargetMapGroup(
                doc.GetValue("_id").AsString,
                ReadLong(doc, "Count"),
                ReadLong(doc, "MissingMapNameCount"),
                ReadLong(doc, "MissingMapIdCount"),
                ReadIntArray(doc, "Seasons"),
                ReadIntArray(doc, "GameModes"),
                ReadObjectIds(doc, "SampleIds", options.SampleIdsPerMap)))
            .ToList();
    }

    private static BsonDocument CountMissingExpression(string fieldName)
    {
        return new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
        {
            new BsonDocument("$eq", new BsonArray { fieldName, BsonNull.Value }),
            1,
            0
        }));
    }

    private static BsonDocument CountMissingOrEmptyExpression(string fieldName)
    {
        return new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
        {
            new BsonDocument("$or", new BsonArray
            {
                new BsonDocument("$eq", new BsonArray { fieldName, BsonNull.Value }),
                new BsonDocument("$eq", new BsonArray { fieldName, string.Empty })
            }),
            1,
            0
        }));
    }

    private static FilterDefinition<BsonDocument> BuildSeasonFilter(int minSeason, int? maxSeason, MapMetadataBackfillOptions options)
    {
        var builder = Builders<BsonDocument>.Filter;
        var filter = builder.Gte("Season", minSeason);
        if (maxSeason.HasValue)
        {
            filter &= builder.Lte("Season", maxSeason.Value);
        }

        if (options.HasGameModeFilter)
        {
            filter &= builder.In("GameMode", options.GameModes);
        }

        return filter;
    }

    private static FilterDefinition<BsonDocument> BuildTargetMapFilter(
        MapMetadataBackfillOptions options,
        TargetMapGroup target)
    {
        return BuildSeasonFilter(options.TargetMinSeason, options.TargetMaxSeason, options)
            & Builders<BsonDocument>.Filter.Eq("Map", target.Map);
    }

    private static async Task<long> ApplyMapMetadataAsync(
        IMongoCollection<BsonDocument> collection,
        MapMetadataBackfillOptions options,
        TargetMapGroup target,
        MapMetadataResolution resolution)
    {
        long modifiedCount = 0;
        var targetMapFilter = BuildTargetMapFilter(options, target);

        if (target.MissingMapNameCount > 0)
        {
            var updates = new List<UpdateDefinition<BsonDocument>>
            {
                Builders<BsonDocument>.Update.Set("MapName", resolution.MapName)
            };

            if (resolution.MapId.HasValue)
            {
                updates.Add(Builders<BsonDocument>.Update.Set("MapId", resolution.MapId.Value));
            }

            var result = await collection.UpdateManyAsync(
                targetMapFilter & MissingMapNameFilter(),
                Builders<BsonDocument>.Update.Combine(updates));
            modifiedCount += result.ModifiedCount;
        }

        if (target.MissingMapIdCount > 0 && resolution.MapId.HasValue)
        {
            var result = await collection.UpdateManyAsync(
                targetMapFilter & MissingMapIdFilter(),
                Builders<BsonDocument>.Update.Set("MapId", resolution.MapId.Value));
            modifiedCount += result.ModifiedCount;
        }

        return modifiedCount;
    }

    private static FilterDefinition<BsonDocument> MissingMapNameFilter()
    {
        return Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("MapName", false),
            Builders<BsonDocument>.Filter.Eq("MapName", BsonNull.Value),
            Builders<BsonDocument>.Filter.Eq("MapName", string.Empty));
    }

    private static FilterDefinition<BsonDocument> MissingMapIdFilter()
    {
        return Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("MapId", false),
            Builders<BsonDocument>.Filter.Eq("MapId", BsonNull.Value));
    }

    private static int ReadInt(BsonDocument doc, string name)
    {
        return doc.GetValue(name).ToInt32();
    }

    private static long ReadLong(BsonDocument doc, string name)
    {
        return doc.GetValue(name).ToInt64();
    }

    private static int? ReadNullableInt(BsonDocument doc, string name)
    {
        var value = doc.GetValue(name, BsonNull.Value);
        return value.IsBsonNull ? null : value.ToInt32();
    }

    private static IReadOnlyCollection<int> ReadIntArray(BsonDocument doc, string name)
    {
        return doc.GetValue(name, new BsonArray())
            .AsBsonArray
            .Where(v => !v.IsBsonNull)
            .Select(v => v.ToInt32())
            .ToList();
    }

    private static IReadOnlyCollection<string> ReadObjectIds(BsonDocument doc, string name, int maxCount)
    {
        return doc.GetValue(name, new BsonArray())
            .AsBsonArray
            .Take(maxCount)
            .Select(v => v.IsObjectId ? v.AsObjectId.ToString() : v.ToString())
            .ToList();
    }

    private sealed record TargetMapGroup(
        string Map,
        long Count,
        long MissingMapNameCount,
        long MissingMapIdCount,
        IReadOnlyCollection<int> Seasons,
        IReadOnlyCollection<int> GameModes,
        IReadOnlyCollection<string> SampleIds);
}
