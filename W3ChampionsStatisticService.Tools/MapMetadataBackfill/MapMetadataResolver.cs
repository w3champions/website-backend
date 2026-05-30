using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace W3ChampionsStatisticService.Tools.MapMetadataBackfill;

public sealed record SourceMapMetadata(
    string Map,
    string MapName,
    int? MapId,
    int MinSeason,
    int MaxSeason,
    IReadOnlyCollection<int> GameModes,
    long Count);

public sealed record ManualMapMetadata(string MapName, int? MapId);

public sealed record MapMetadataResolution(
    MapMetadataResolutionStatus Status,
    string Confidence,
    string MapName,
    int? MapId,
    IReadOnlyCollection<string> SourceMaps,
    string Notes)
{
    public bool CanApply => Status == MapMetadataResolutionStatus.Resolved;
}

public enum MapMetadataResolutionStatus
{
    Resolved,
    Ambiguous,
    Missing
}

public sealed class MapMetadataResolver
{
    private readonly int _preferredNameSeason;
    private readonly Dictionary<string, List<SourceMapMetadata>> _exactSourceIndex;
    private readonly Dictionary<string, List<SourceMapMetadata>> _stableSourceIndex;
    private readonly Dictionary<string, List<SourceMapMetadata>> _familySourceIndex;
    private readonly Dictionary<string, ManualMapMetadata> _manualExactIndex;
    private readonly Dictionary<string, ManualMapMetadata> _manualStableIndex;
    private readonly Dictionary<string, ManualMapMetadata> _manualFamilyIndex;

    public MapMetadataResolver(
        IEnumerable<SourceMapMetadata> sourceMetadata,
        IEnumerable<KeyValuePair<string, ManualMapMetadata>> manualMetadata = null,
        int preferredNameSeason = MapMetadataBackfillOptions.DefaultPreferredNameSeason)
    {
        _preferredNameSeason = preferredNameSeason;
        var source = sourceMetadata?.ToList() ?? new List<SourceMapMetadata>();
        _exactSourceIndex = BuildSourceIndex(source, MapKeyNormalizer.ExactKey);
        _stableSourceIndex = BuildSourceIndex(source, MapKeyNormalizer.StableKey);
        _familySourceIndex = BuildSourceIndex(source, MapKeyNormalizer.FamilyKey);
        var manual = BuiltInManualMetadata().Concat(manualMetadata ?? Array.Empty<KeyValuePair<string, ManualMapMetadata>>()).ToList();
        _manualExactIndex = BuildManualIndex(manual, MapKeyNormalizer.ExactKey);
        _manualStableIndex = BuildManualIndex(manual, MapKeyNormalizer.StableKey);
        _manualFamilyIndex = BuildManualIndex(manual, MapKeyNormalizer.FamilyKey);
    }

    public MapMetadataResolution Resolve(string map)
    {
        if (string.IsNullOrWhiteSpace(map))
        {
            return Missing("Map is empty.");
        }

        var exact = ResolveFromSource(_exactSourceIndex, MapKeyNormalizer.ExactKey(map), "exact");
        if (exact != null)
        {
            return exact;
        }

        var stable = ResolveFromSource(_stableSourceIndex, MapKeyNormalizer.StableKey(map), "normalized");
        if (stable != null)
        {
            return stable;
        }

        var manual = ResolveManual(map);
        if (manual != null)
        {
            return manual;
        }

        var family = ResolveFromSource(_familySourceIndex, MapKeyNormalizer.FamilyKey(map), "family");
        if (family != null)
        {
            return family;
        }

        return Missing("No catalog or manual mapping matched.");
    }

    public static IReadOnlyCollection<KeyValuePair<string, ManualMapMetadata>> LoadManualMetadata(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Array.Empty<KeyValuePair<string, ManualMapMetadata>>();
        }

        var json = File.ReadAllText(path);
        var values = JsonSerializer.Deserialize<Dictionary<string, ManualMapMetadataFileEntry>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (values == null)
        {
            return Array.Empty<KeyValuePair<string, ManualMapMetadata>>();
        }

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v.Key) && !string.IsNullOrWhiteSpace(v.Value?.MapName))
            .Select(v => new KeyValuePair<string, ManualMapMetadata>(v.Key, new ManualMapMetadata(v.Value.MapName, v.Value.MapId)))
            .ToList();
    }

    private MapMetadataResolution ResolveManual(string map)
    {
        var exactKey = MapKeyNormalizer.ExactKey(map);
        if (_manualExactIndex.TryGetValue(exactKey, out var exactMetadata))
        {
            return ManualResolution(exactMetadata);
        }

        var stableKey = MapKeyNormalizer.StableKey(map);
        if (_manualStableIndex.TryGetValue(stableKey, out var stableMetadata))
        {
            return ManualResolution(stableMetadata);
        }

        var familyKey = MapKeyNormalizer.FamilyKey(map);
        if (_manualFamilyIndex.TryGetValue(familyKey, out var familyMetadata))
        {
            return ManualResolution(familyMetadata);
        }

        return null;
    }

    private static MapMetadataResolution ManualResolution(ManualMapMetadata metadata)
    {
        return new MapMetadataResolution(
            MapMetadataResolutionStatus.Resolved,
            "manual",
            metadata.MapName,
            metadata.MapId,
            Array.Empty<string>(),
            "Matched built-in or supplied manual metadata.");
    }

    private MapMetadataResolution ResolveFromSource(
        IReadOnlyDictionary<string, List<SourceMapMetadata>> index,
        string key,
        string confidence)
    {
        if (string.IsNullOrWhiteSpace(key) || !index.TryGetValue(key, out var candidates) || candidates.Count == 0)
        {
            return null;
        }

        var groupedByIdentity = candidates
            .GroupBy(IdentityKey)
            .ToList();

        if (groupedByIdentity.Count > 1)
        {
            var sources = candidates
                .OrderBy(c => c.MinSeason)
                .ThenBy(c => c.Map)
                .Select(SourceLabel)
                .Distinct()
                .ToList();

            return new MapMetadataResolution(
                MapMetadataResolutionStatus.Ambiguous,
                confidence,
                null,
                null,
                sources,
                "Matched multiple map identities. Add a manual mapping before applying.");
        }

        var selected = SelectPreferredNameCandidate(candidates);
        var distinctNameCount = candidates.Select(c => c.MapName).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        return new MapMetadataResolution(
            MapMetadataResolutionStatus.Resolved,
            confidence,
            selected.MapName,
            selected.MapId,
            candidates.Select(SourceLabel).Distinct().OrderBy(s => s).ToList(),
            distinctNameCount > 1
                ? PreferredNameNote(selected)
                : "Matched source metadata.");
    }

    private SourceMapMetadata SelectPreferredNameCandidate(IReadOnlyCollection<SourceMapMetadata> candidates)
    {
        return candidates
            .OrderByDescending(CoversPreferredNameSeason)
            .ThenByDescending(c => c.MaxSeason)
            .ThenByDescending(c => c.Count)
            .ThenBy(c => c.MapName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Map, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private bool CoversPreferredNameSeason(SourceMapMetadata source)
    {
        return source.MinSeason <= _preferredNameSeason && source.MaxSeason >= _preferredNameSeason;
    }

    private string PreferredNameNote(SourceMapMetadata selected)
    {
        if (CoversPreferredNameSeason(selected))
        {
            return $"Matched one map id with multiple display names; using season {_preferredNameSeason} source-season name.";
        }

        return $"Matched one map id with multiple display names; no season {_preferredNameSeason} name found, using latest source-season name.";
    }

    private static string IdentityKey(SourceMapMetadata source)
    {
        if (source.MapId.HasValue)
        {
            return $"id:{source.MapId.Value}";
        }

        return $"name:{MapKeyNormalizer.StableKey(source.MapName)}";
    }

    private static string SourceLabel(SourceMapMetadata source)
    {
        var mapId = source.MapId.HasValue ? source.MapId.Value.ToString() : "null";
        return $"{source.Map} -> {source.MapName} (id {mapId}, s{source.MinSeason}-{source.MaxSeason})";
    }

    private static MapMetadataResolution Missing(string notes)
    {
        return new MapMetadataResolution(
            MapMetadataResolutionStatus.Missing,
            "none",
            null,
            null,
            Array.Empty<string>(),
            notes);
    }

    private static Dictionary<string, List<SourceMapMetadata>> BuildSourceIndex(
        IEnumerable<SourceMapMetadata> source,
        Func<string, string> keySelector)
    {
        var result = new Dictionary<string, List<SourceMapMetadata>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in source)
        {
            var key = keySelector(item.Map);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!result.TryGetValue(key, out var bucket))
            {
                bucket = new List<SourceMapMetadata>();
                result[key] = bucket;
            }

            bucket.Add(item);
        }

        return result;
    }

    private static Dictionary<string, ManualMapMetadata> BuildManualIndex(
        IEnumerable<KeyValuePair<string, ManualMapMetadata>> manualMetadata,
        Func<string, string> keySelector)
    {
        var result = new Dictionary<string, ManualMapMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manualMetadata)
        {
            var key = keySelector(item.Key);
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = item.Value;
            }
        }

        return result;
    }

    private static IReadOnlyCollection<KeyValuePair<string, ManualMapMetadata>> BuiltInManualMetadata()
    {
        return new Dictionary<string, ManualMapMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["amazonia"] = new("Amazonia", 0),
            ["Amazonia"] = new("Amazonia", 0),
            ["concealedhill"] = new("Concealed Hill", 1),
            ["ConcealedHill"] = new("Concealed Hill", 1),
            ["echoisles"] = new("Echo Isles", 2),
            ["EchoIsles"] = new("Echo Isles", 2),
            ["lastrefuge"] = new("Last Refuge", 3),
            ["LastRefuge"] = new("Last Refuge", 3),
            ["northernisles"] = new("Northern Isles", 4),
            ["NorthernIsles"] = new("Northern Isles", 4),
            ["terenasstand"] = new("Terenas Stand LV", 5),
            ["TerenasStand"] = new("Terenas Stand LV", 5),
            ["TerenasStandLV"] = new("Terenas Stand LV", 5),
            ["twistedmeadows"] = new("Twisted Meadows", 6),
            ["TwistedMeadows"] = new("Twisted Meadows", 6),
            ["turtlerock"] = new("Turtle Rock", 12),
            ["TurtleRock"] = new("Turtle Rock", 12),
            ["autumnleaves"] = new("Autumn Leaves", 44),
            ["AutumnLeaves"] = new("Autumn Leaves", 44),
            ["autumnleaves201016"] = new("Autumn Leaves", 44),
            ["AutumnLeavesv2-0"] = new("Autumn Leaves", 44),
            ["tidehunters"] = new("Tidehunters", 54),
            ["Tidehunters"] = new("Tidehunters", 54),
            ["ShatteredExile"] = new("Shattered Exile", 59),
            ["ShatteredExilev2_06"] = new("Shattered Exile", 59),
            ["ShatteredExilev2-07"] = new("Shattered Exile", 59),
            ["ShallowGrave"] = new("Shallow Grave", 61),
            ["ShallowGravev1_4"] = new("Shallow Grave", 61),
            ["ruinsofazshara"] = new("Ruins of Azshara", null),
            ["RuinsOfAzshara"] = new("Ruins of Azshara", null),
            ["ruinsofazshara201016"] = new("Ruins of Azshara LV", null)
        };
    }

    private sealed class ManualMapMetadataFileEntry
    {
        public string MapName { get; set; }
        public int? MapId { get; set; }
    }
}

public static class MapKeyNormalizer
{
    public static string ExactKey(string value)
    {
        return value?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    public static string StableKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var key = Path.GetFileName(value.Trim()).ToLowerInvariant();
        key = Regex.Replace(key, @"\.(w3x|w3m)$", string.Empty, RegexOptions.IgnoreCase);
        key = Regex.Replace(key, @"^s\d+[_-]?\d*", string.Empty, RegexOptions.IgnoreCase);
        key = Regex.Replace(key, @"^\d{10}", string.Empty, RegexOptions.IgnoreCase);
        key = Regex.Replace(key, @"^\d?c\d{10}", string.Empty, RegexOptions.IgnoreCase);
        key = Regex.Replace(key, @"^\d?w3c\d{10}", string.Empty, RegexOptions.IgnoreCase);
        key = Regex.Replace(key, @"w3c\d{10,}$", string.Empty, RegexOptions.IgnoreCase);
        return Regex.Replace(key, @"[^a-z0-9]", string.Empty, RegexOptions.IgnoreCase);
    }

    public static string FamilyKey(string value)
    {
        var key = StableKey(value);
        if (string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        key = Regex.Replace(key, @"20\d{4}$", string.Empty, RegexOptions.IgnoreCase);
        key = Regex.Replace(key, @"v\d+[a-z0-9]*$", string.Empty, RegexOptions.IgnoreCase);
        key = Regex.Replace(key, @"lv$", string.Empty, RegexOptions.IgnoreCase);
        return key;
    }

}
