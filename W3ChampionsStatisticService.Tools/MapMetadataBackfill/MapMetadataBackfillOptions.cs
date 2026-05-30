using System;
using System.Collections.Generic;
using System.Linq;

namespace W3ChampionsStatisticService.Tools.MapMetadataBackfill;

public sealed class MapMetadataBackfillOptions
{
    public const string DefaultDatabaseName = "W3Champions-Statistic-Service";
    public const string DefaultCollectionName = "Matchup";
    public const int DefaultPreferredNameSeason = 24;

    public string ConnectionString { get; init; }
    public string DatabaseName { get; init; } = DefaultDatabaseName;
    public string CollectionName { get; init; } = DefaultCollectionName;
    public int TargetMinSeason { get; init; } = 1;
    public int TargetMaxSeason { get; init; } = 10;
    public int SourceMinSeason { get; init; } = 11;
    public int? SourceMaxSeason { get; init; }
    public int PreferredNameSeason { get; init; } = DefaultPreferredNameSeason;
    public IReadOnlyCollection<int> GameModes { get; init; } = Array.Empty<int>();
    public bool Apply { get; init; }
    public bool RequireMapId { get; init; }
    public int SampleIdsPerMap { get; init; } = 3;
    public int PreviewLimit { get; init; } = 25;
    public string ReportPath { get; init; } = "map-metadata-backfill-report.csv";
    public string ManualMapPath { get; init; }

    public bool HasGameModeFilter => GameModes.Count > 0;

    public static string Usage =>
        """
        Usage:
          dotnet run --project W3ChampionsStatisticService.Tools -- backfill-map-metadata [options]

        Required:
          --connection-string <mongodb-uri>    MongoDB URI. If omitted, MONGO_CONNECTION_STRING is used.

        Options:
          --database <name>                    Defaults to W3Champions-Statistic-Service.
          --collection <name>                  Defaults to Matchup.
          --season <number>                    Backfill one target season. Equivalent to min/max season.
          --target-min-season <number>         Defaults to 1.
          --target-max-season <number>         Defaults to 10.
          --source-min-season <number>         Defaults to 11.
          --source-max-season <number>         Optional upper bound for catalog rows.
          --preferred-name-season <number>     Source season to prefer for display names. Defaults to 24.
          --game-mode <ids>                    Optional comma-separated list, for example 1 or 1,2,4.
          --report <path>                      CSV report path. Defaults to map-metadata-backfill-report.csv.
          --manual-map <path>                  Optional JSON file with extra legacy map mappings.
          --sample-ids-per-map <number>        Defaults to 3.
          --preview-limit <number>             Console preview rows. Defaults to 25. Use 0 to hide.
          --require-map-id                     Skip rows where only MapName can be inferred.
          --apply                              Write safe matches. Without this, the command is a dry run.
          --help                               Show this help.

        Manual map JSON shape:
          {
            "LegacyMapKey": { "mapName": "Display Name", "mapId": 123 }
          }
        """;

    public static MapMetadataBackfillOptions Parse(string[] args)
    {
        var queue = new Queue<string>(args);
        if (queue.Count > 0 && queue.Peek().Equals("backfill-map-metadata", StringComparison.OrdinalIgnoreCase))
        {
            queue.Dequeue();
        }

        string connectionString = null;
        string databaseName = DefaultDatabaseName;
        string collectionName = DefaultCollectionName;
        int targetMinSeason = 1;
        int targetMaxSeason = 10;
        int? targetSeason = null;
        int sourceMinSeason = 11;
        int? sourceMaxSeason = null;
        int preferredNameSeason = DefaultPreferredNameSeason;
        IReadOnlyCollection<int> gameModes = Array.Empty<int>();
        bool apply = false;
        bool requireMapId = false;
        int sampleIdsPerMap = 3;
        int previewLimit = 25;
        string reportPath = "map-metadata-backfill-report.csv";
        string manualMapPath = null;

        while (queue.Count > 0)
        {
            var arg = queue.Dequeue();
            switch (arg)
            {
                case "--help":
                case "-h":
                    throw new HelpRequestedException();
                case "--connection-string":
                    connectionString = ReadValue(queue, arg);
                    break;
                case "--database":
                    databaseName = ReadValue(queue, arg);
                    break;
                case "--collection":
                    collectionName = ReadValue(queue, arg);
                    break;
                case "--season":
                    targetSeason = ReadInt(queue, arg);
                    break;
                case "--target-min-season":
                    targetMinSeason = ReadInt(queue, arg);
                    break;
                case "--target-max-season":
                    targetMaxSeason = ReadInt(queue, arg);
                    break;
                case "--source-min-season":
                    sourceMinSeason = ReadInt(queue, arg);
                    break;
                case "--source-max-season":
                    sourceMaxSeason = ReadInt(queue, arg);
                    break;
                case "--preferred-name-season":
                    preferredNameSeason = ReadInt(queue, arg);
                    break;
                case "--game-mode":
                case "--game-modes":
                    gameModes = ParseIntList(ReadValue(queue, arg), arg);
                    break;
                case "--report":
                    reportPath = ReadValue(queue, arg);
                    break;
                case "--manual-map":
                    manualMapPath = ReadValue(queue, arg);
                    break;
                case "--sample-ids-per-map":
                    sampleIdsPerMap = ReadInt(queue, arg);
                    break;
                case "--preview-limit":
                    previewLimit = ReadInt(queue, arg);
                    break;
                case "--require-map-id":
                    requireMapId = true;
                    break;
                case "--apply":
                    apply = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }

        if (targetSeason.HasValue)
        {
            targetMinSeason = targetSeason.Value;
            targetMaxSeason = targetSeason.Value;
        }

        connectionString ??= Environment.GetEnvironmentVariable("MONGO_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Missing MongoDB connection string. Pass --connection-string or set MONGO_CONNECTION_STRING.");
        }

        if (targetMinSeason > targetMaxSeason)
        {
            throw new ArgumentException("--target-min-season must be less than or equal to --target-max-season.");
        }

        if (sourceMaxSeason.HasValue && sourceMinSeason > sourceMaxSeason.Value)
        {
            throw new ArgumentException("--source-min-season must be less than or equal to --source-max-season.");
        }

        if (preferredNameSeason < 0)
        {
            throw new ArgumentException("--preferred-name-season must be zero or greater.");
        }

        if (sampleIdsPerMap < 0)
        {
            throw new ArgumentException("--sample-ids-per-map must be zero or greater.");
        }

        if (previewLimit < 0)
        {
            throw new ArgumentException("--preview-limit must be zero or greater.");
        }

        return new MapMetadataBackfillOptions
        {
            ConnectionString = connectionString,
            DatabaseName = databaseName,
            CollectionName = collectionName,
            TargetMinSeason = targetMinSeason,
            TargetMaxSeason = targetMaxSeason,
            SourceMinSeason = sourceMinSeason,
            SourceMaxSeason = sourceMaxSeason,
            PreferredNameSeason = preferredNameSeason,
            GameModes = gameModes,
            Apply = apply,
            RequireMapId = requireMapId,
            SampleIdsPerMap = sampleIdsPerMap,
            PreviewLimit = previewLimit,
            ReportPath = reportPath,
            ManualMapPath = manualMapPath
        };
    }

    private static string ReadValue(Queue<string> queue, string option)
    {
        if (queue.Count == 0)
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return queue.Dequeue();
    }

    private static int ReadInt(Queue<string> queue, string option)
    {
        var value = ReadValue(queue, option);
        if (!int.TryParse(value, out var result))
        {
            throw new ArgumentException($"{option} requires a numeric value.");
        }

        return result;
    }

    private static IReadOnlyCollection<int> ParseIntList(string value, string option)
    {
        var parsed = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v =>
            {
                if (!int.TryParse(v, out var id))
                {
                    throw new ArgumentException($"{option} contains a non-numeric value: '{v}'.");
                }

                return id;
            })
            .Distinct()
            .ToArray();

        if (parsed.Length == 0)
        {
            throw new ArgumentException($"{option} requires at least one value.");
        }

        return parsed;
    }
}

public sealed class HelpRequestedException : Exception
{
}
