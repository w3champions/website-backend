using System;
using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.Tools.MapMetadataBackfill;

namespace W3ChampionsStatisticService.Tools.MapKeyBackfill;

public sealed class MapKeyBackfillOptions
{
    public const string DefaultDatabaseName = "W3Champions-Statistic-Service";
    public const string DefaultReportPath = "map-key-backfill-report.csv";
    public const int DefaultBatchSize = 1000;
    public const int DefaultAggregationPauseMs = 2000;

    public static readonly IReadOnlyList<string> SupportedCollections = new[]
    {
        "Matchup",
        "MapsPerSeason",
        "PlayerHeroStats",
        "PlayerRaceOnMapVersusRaceRatio",
        "OverallRaceAndWinStat"
    };

    public string ConnectionString { get; init; }
    public string DatabaseName { get; init; } = DefaultDatabaseName;
    public IReadOnlyList<string> Collections { get; init; } = SupportedCollections;
    public bool Apply { get; init; }
    public string ReportPath { get; init; } = DefaultReportPath;
    public string ManualMapPath { get; init; }
    public string UpdateServiceMapDataPath { get; init; }
    public string MmMapsPath { get; init; }
    public int BatchSize { get; init; } = DefaultBatchSize;
    public string StartAfter { get; init; }
    public string EventCatalogPath { get; init; }
    public string WriteEventCatalogPath { get; init; }
    public int AggregationPauseMs { get; init; } = DefaultAggregationPauseMs;
    public int BatchPauseMs { get; init; }
    public bool AllowRemote { get; init; }
    public string ConfirmDatabase { get; init; }

    public static string Usage =>
        """
        Usage:
          dotnet run --project W3ChampionsStatisticService.Tools -- backfill-map-keys [options]

        Rewrites legacy mangled map keys into the map file name, MapId and MapName.

        Required:
          --connection-string <mongodb-uri>    MongoDB URI. If omitted, MONGO_CONNECTION_STRING is used.

        Options:
          --database <name>                    Defaults to W3Champions-Statistic-Service.
          --collections <names>                Comma-separated list. Defaults to
                                               Matchup,MapsPerSeason,PlayerHeroStats,PlayerRaceOnMapVersusRaceRatio,OverallRaceAndWinStat.
          --report <path>                      CSV report path. Defaults to map-key-backfill-report.csv.
          --manual-map <path>                  Optional JSON file with manual annotations (shape below).
          --update-service-map-data <path>     mongoexport of UpdateService MapData.
          --mm-maps <path>                     mongoexport of matchmaking-service Maps.
          --event-catalog <path>               Read the MatchFinishedEvent map catalog from a file written by
                                               --write-event-catalog instead of aggregating the events.
          --write-event-catalog <path>         Aggregate the events and save the catalog to this file.
          --aggregation-pause-ms <number>      Pause between the monthly event aggregations. Defaults to 2000.
          --batch-size <number>                Documents per bulk write. Defaults to 1000.
          --batch-pause-ms <number>            Pause between bulk writes. Defaults to 0.
          --start-after <_id>                  Resume after a document id. Needs exactly one collection in --collections,
                                               and not MapsPerSeason or OverallRaceAndWinStat (always processed whole).
          --apply                              Write changes. Without this, the command is a dry run.
          --allow-remote                       Allow --apply on a host other than localhost/127.0.0.1
                                               (or on a localhost URI whose server is another machine).
          --confirm-database <name>            Required with --allow-remote; must equal the target database.
          --help                               Show this help.

        Manual map JSON shape: one entry per map, listing every stored spelling of its legacy key. An entry applies
        only in its seasons and, if gameModes is given, only in those modes. mapId null keeps the fixed mapName.
          [
            { "mapId": 3, "mapName": "Last Refuge", "keys": ["LastRefuge", "lastrefuge"], "seasons": "0-10" },
            { "mapId": 19, "mapName": "Twilight Ruins", "keys": ["twilightruins"], "seasons": "1-5", "gameModes": ["FFA"] }
          ]
        """;

    public static MapKeyBackfillOptions Parse(string[] args)
    {
        var queue = new Queue<string>(args);
        if (queue.Count > 0 && queue.Peek().Equals("backfill-map-keys", StringComparison.OrdinalIgnoreCase))
        {
            queue.Dequeue();
        }

        string connectionString = null;
        string databaseName = DefaultDatabaseName;
        IReadOnlyList<string> collections = SupportedCollections;
        bool apply = false;
        string reportPath = DefaultReportPath;
        string manualMapPath = null;
        string updateServiceMapDataPath = null;
        string mmMapsPath = null;
        int batchSize = DefaultBatchSize;
        string startAfter = null;
        string eventCatalogPath = null;
        string writeEventCatalogPath = null;
        int aggregationPauseMs = DefaultAggregationPauseMs;
        int batchPauseMs = 0;
        bool allowRemote = false;
        string confirmDatabase = null;

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
                case "--collections":
                    collections = ParseCollections(ReadValue(queue, arg), arg);
                    break;
                case "--report":
                    reportPath = ReadValue(queue, arg);
                    break;
                case "--manual-map":
                    manualMapPath = ReadValue(queue, arg);
                    break;
                case "--update-service-map-data":
                    updateServiceMapDataPath = ReadValue(queue, arg);
                    break;
                case "--mm-maps":
                    mmMapsPath = ReadValue(queue, arg);
                    break;
                case "--batch-size":
                    batchSize = ReadInt(queue, arg);
                    break;
                case "--start-after":
                    startAfter = ReadValue(queue, arg);
                    break;
                case "--event-catalog":
                    eventCatalogPath = ReadValue(queue, arg);
                    break;
                case "--write-event-catalog":
                    writeEventCatalogPath = ReadValue(queue, arg);
                    break;
                case "--aggregation-pause-ms":
                    aggregationPauseMs = ReadInt(queue, arg);
                    break;
                case "--batch-pause-ms":
                    batchPauseMs = ReadInt(queue, arg);
                    break;
                case "--apply":
                    apply = true;
                    break;
                case "--allow-remote":
                    allowRemote = true;
                    break;
                case "--confirm-database":
                    confirmDatabase = ReadValue(queue, arg);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }

        connectionString ??= Environment.GetEnvironmentVariable("MONGO_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Missing MongoDB connection string. Pass --connection-string or set MONGO_CONNECTION_STRING.");
        }

        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new ArgumentException("--database must not be empty.");
        }

        if (batchSize <= 0)
        {
            throw new ArgumentException("--batch-size must be greater than zero.");
        }

        if (startAfter != null && collections.Count != 1)
        {
            throw new ArgumentException("--start-after requires exactly one collection in --collections.");
        }

        if (startAfter != null && collections[0] is "MapsPerSeason" or "OverallRaceAndWinStat")
        {
            throw new ArgumentException($"--start-after can't be used with {collections[0]}: it is always processed whole.");
        }

        if (startAfter != null && collections[0] == "Matchup" && !MongoDB.Bson.ObjectId.TryParse(startAfter, out _))
        {
            throw new ArgumentException("--start-after for Matchup must be an ObjectId.");
        }

        if (eventCatalogPath != null && writeEventCatalogPath != null)
        {
            throw new ArgumentException("--event-catalog and --write-event-catalog can't be used together.");
        }

        if (aggregationPauseMs < 0 || batchPauseMs < 0)
        {
            throw new ArgumentException("--aggregation-pause-ms and --batch-pause-ms must not be negative.");
        }

        if (allowRemote != (confirmDatabase != null))
        {
            throw new ArgumentException("--allow-remote and --confirm-database must be given together.");
        }

        return new MapKeyBackfillOptions
        {
            ConnectionString = connectionString,
            DatabaseName = databaseName,
            Collections = collections,
            Apply = apply,
            ReportPath = reportPath,
            ManualMapPath = manualMapPath,
            UpdateServiceMapDataPath = updateServiceMapDataPath,
            MmMapsPath = mmMapsPath,
            BatchSize = batchSize,
            StartAfter = startAfter,
            EventCatalogPath = eventCatalogPath,
            WriteEventCatalogPath = writeEventCatalogPath,
            AggregationPauseMs = aggregationPauseMs,
            BatchPauseMs = batchPauseMs,
            AllowRemote = allowRemote,
            ConfirmDatabase = confirmDatabase
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

    private static IReadOnlyList<string> ParseCollections(string value, string option)
    {
        var parsed = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name =>
            {
                var supported = SupportedCollections.FirstOrDefault(c => c.Equals(name, StringComparison.Ordinal));
                if (supported == null)
                {
                    throw new ArgumentException($"{option} contains an unsupported collection: '{name}'. Supported: {string.Join(", ", SupportedCollections)}.");
                }

                return supported;
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
