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
          --manual-map <path>                  Optional JSON file with manual annotations.
          --update-service-map-data <path>     mongoexport of UpdateService MapData.
          --mm-maps <path>                     mongoexport of matchmaking-service Maps.
          --batch-size <number>                Documents per bulk write. Defaults to 1000.
          --start-after <_id>                  Resume after a document id. Needs exactly one collection in --collections.
          --apply                              Write changes. Without this, the command is a dry run.
          --allow-remote                       Allow --apply on a host other than localhost/127.0.0.1.
          --confirm-database <name>            Required with --allow-remote; must equal the target database.
          --help                               Show this help.

        Manual map JSON shape (keys are legacy keys or file names):
          {
            "LegacyMapKey": { "mapName": "Display Name", "mapId": 123 }
          }
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
