using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace W3ChampionsStatisticService.Tools.MapKeyBackfill;

// One entry per map (id, or a fixed name for an older version without an id of its own), with every stored
// spelling of its legacy key. An entry applies only in its seasons and, when given, its game modes.
public sealed record ManualMapEntry(
    int? MapId,
    string MapName,
    IReadOnlyList<string> Keys,
    IReadOnlySet<int> Seasons,
    IReadOnlySet<int> GameModes,
    long Games,
    string SeasonsText)
{
    public bool AppliesTo(string key, int? season, int? gameMode)
    {
        return Keys.Contains(key, StringComparer.Ordinal)
            && (season == null || Seasons.Contains(season.Value))
            && (gameMode == null || GameModes == null || GameModes.Contains(gameMode.Value));
    }
}

public sealed class ManualMap
{
    public static readonly ManualMap Empty = new(Array.Empty<ManualMapEntry>());

    private readonly Dictionary<string, List<ManualMapEntry>> _byKey;

    public ManualMap(IReadOnlyList<ManualMapEntry> entries)
    {
        Entries = entries;
        _byKey = new Dictionary<string, List<ManualMapEntry>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            foreach (var key in entry.Keys.Distinct(StringComparer.Ordinal))
            {
                if (!_byKey.TryGetValue(key, out var list))
                {
                    list = new List<ManualMapEntry>();
                    _byKey[key] = list;
                }

                list.Add(entry);
            }
        }
    }

    public IReadOnlyList<ManualMapEntry> Entries { get; }

    // season null = any season (collections without a season); gameMode null = any mode.
    public IReadOnlyList<ManualMapEntry> Match(string key, int? season, int? gameMode)
    {
        if (key == null || !_byKey.TryGetValue(key, out var candidates))
        {
            return Array.Empty<ManualMapEntry>();
        }

        return candidates.Where(e => e.AppliesTo(key, season, gameMode)).ToList();
    }

    public static ManualMap Load(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? Empty : Parse(File.ReadAllText(path));
    }

    public static ManualMap Parse(string json)
    {
        var lines = JsonSerializer.Deserialize<List<ManualMapFileEntry>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new List<ManualMapFileEntry>();

        var entries = lines.Select((line, index) =>
        {
            if (line?.Keys == null || line.Keys.Count == 0 || line.Keys.Any(string.IsNullOrWhiteSpace))
            {
                throw new FormatException($"Manual map entry {index + 1} needs a non-empty 'keys' list.");
            }

            if (string.IsNullOrWhiteSpace(line.Seasons))
            {
                throw new FormatException($"Manual map entry {index + 1} ({line.Keys[0]}) needs 'seasons'.");
            }

            return new ManualMapEntry(
                line.MapId,
                string.IsNullOrWhiteSpace(line.MapName) ? null : line.MapName,
                line.Keys,
                ParseSeasons(line.Seasons),
                line.GameModes == null ? null : line.GameModes.Select(GameModeNames.Parse).ToHashSet(),
                line.Games ?? 0,
                line.Seasons);
        }).ToList();

        return new ManualMap(entries);
    }

    // "3", "0-10", "0-11,24", "7-16,18-25"
    public static IReadOnlySet<int> ParseSeasons(string text)
    {
        var seasons = new HashSet<int>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bounds = part.Split('-', StringSplitOptions.TrimEntries);
            if (bounds.Length > 2
                || !int.TryParse(bounds[0], out var from)
                || !int.TryParse(bounds[^1], out var to)
                || from > to)
            {
                throw new FormatException($"Invalid seasons '{text}'.");
            }

            for (var season = from; season <= to; season++)
            {
                seasons.Add(season);
            }
        }

        if (seasons.Count == 0)
        {
            throw new FormatException($"Invalid seasons '{text}'.");
        }

        return seasons;
    }

    private sealed class ManualMapFileEntry
    {
        public int? MapId { get; set; }
        public string MapName { get; set; }
        public List<string> Keys { get; set; }
        public string Seasons { get; set; }
        public List<string> GameModes { get; set; }
        public long? Games { get; set; }
    }
}
