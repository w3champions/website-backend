using System;
using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.Tools.MapMetadataBackfill;

namespace W3ChampionsStatisticService.Tools.MapKeyBackfill;

public enum MapKeyStatus
{
    Migrated,
    Resolved,
    ResolvedByCatalog,
    ResolvedManual,
    AmbiguousMerged,
    LegacyKey,
    Unresolved,
    Rebuilt,
    NotRebuilt
}

public static class MapKeyStatusNames
{
    public static string ToCsv(this MapKeyStatus status) => status switch
    {
        MapKeyStatus.Migrated => "migrated",
        MapKeyStatus.Resolved => "resolved",
        MapKeyStatus.ResolvedByCatalog => "resolved-by-catalog",
        MapKeyStatus.ResolvedManual => "resolved-manual",
        MapKeyStatus.AmbiguousMerged => "ambiguous-merged",
        MapKeyStatus.LegacyKey => "legacy-key",
        MapKeyStatus.Unresolved => "unresolved",
        MapKeyStatus.Rebuilt => "rebuilt",
        MapKeyStatus.NotRebuilt => "not-rebuilt",
        _ => status.ToString()
    };
}

// What a collection knows about an entry besides its key.
public enum MapKeyScope
{
    // Matchup and MapsPerSeason mode buckets.
    SeasonAndMode,

    // Hero and race stats, and the MapsPerSeason all-modes bucket: they record no mode.
    SeasonOnly,

    // OverallRaceAndWinStat: patch keys instead of seasons, 1v1 only.
    AllSeasonsOneVsOne
}

// Map is what the entry's Map becomes: a file name, or the legacy key when no file is known.
public sealed record MapKeyResolution(
    MapKeyStatus Status,
    string Map,
    int? MapId,
    string MapName,
    IReadOnlyList<string> CandidateFiles,
    string Source,
    string Notes)
{
    public bool ChangesEntry => Status is not (MapKeyStatus.Migrated or MapKeyStatus.Unresolved);

    public static readonly MapKeyResolution Migrated = new(MapKeyStatus.Migrated, null, null, null, Array.Empty<string>(), "", "");
}

public sealed class MapKeyResolver
{
    private readonly Dictionary<(string Key, int Season), List<EventMapGroup>> _bySeasonAndKey = new();
    private readonly Dictionary<string, List<EventMapGroup>> _byKey = new(StringComparer.Ordinal);
    private readonly HashSet<string> _knownFiles = new(StringComparer.Ordinal);
    private readonly HashSet<int> _eventSeasons = new();
    private readonly IReadOnlyDictionary<string, int> _mapDataIds;
    private readonly IReadOnlyDictionary<int, string> _mmNames;
    private readonly ManualMap _manualMap;
    private readonly MapMetadataResolver _metadataResolver;
    private readonly Dictionary<(MapKeyScope, string, int?, int?), MapKeyResolution> _cache = new();

    public MapKeyResolver(
        IEnumerable<EventMapGroup> events,
        IReadOnlyDictionary<string, int> mapDataIds = null,
        IReadOnlyDictionary<int, string> mmNames = null,
        ManualMap manualMap = null)
    {
        _mapDataIds = mapDataIds ?? new Dictionary<string, int>();
        _mmNames = mmNames ?? new Dictionary<int, string>();
        _manualMap = manualMap ?? ManualMap.Empty;

        var sources = new List<SourceMapMetadata>();
        foreach (var group in events ?? Array.Empty<EventMapGroup>())
        {
            if (group.File == null)
            {
                continue;
            }

            _knownFiles.Add(group.File);
            _eventSeasons.Add(group.Season);
            if (group.LegacyKey == null)
            {
                continue;
            }

            Add(_bySeasonAndKey, (group.LegacyKey, group.Season), group);
            Add(_byKey, group.LegacyKey, group);
            if (!string.IsNullOrWhiteSpace(group.MapName))
            {
                sources.Add(new SourceMapMetadata(group.LegacyKey, group.MapName, group.MapId, group.Season, group.Season, new[] { group.GameMode }, group.Count));
            }
        }

        foreach (var file in _mapDataIds.Keys)
        {
            _knownFiles.Add(file);
        }

        _metadataResolver = new MapMetadataResolver(sources);
    }

    public IReadOnlyCollection<int> EventSeasons => _eventSeasons;

    // Migrated-entry rule by value: an entry is in the new shape when its Map is a known map file name.
    public bool IsKnownFile(string map) => map != null && _knownFiles.Contains(map);

    public bool HasEvents(int season) => _eventSeasons.Contains(season);

    public int? MapDataId(string file) => file != null && _mapDataIds.TryGetValue(file, out var id) ? id : null;

    public string MmName(int? mapId) => mapId != null && _mmNames.TryGetValue(mapId.Value, out var name) ? name : null;

    public ManualMapEntry BestManualEntry(string key, int? season, int? gameMode)
    {
        return _manualMap.Match(key, season, gameMode).OrderByDescending(e => e.Games).FirstOrDefault();
    }

    public MapKeyResolution Resolve(MapKeyScope scope, string key, int? season, int? gameMode)
    {
        if (string.IsNullOrEmpty(key))
        {
            return new MapKeyResolution(MapKeyStatus.Unresolved, key, null, null, Array.Empty<string>(), "none", "Map is empty.");
        }

        if (IsKnownFile(key))
        {
            return MapKeyResolution.Migrated;
        }

        var cacheKey = (scope, key, season, gameMode);
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var candidates = Candidates(scope, key, season, gameMode);
        var manual = scope switch
        {
            MapKeyScope.SeasonAndMode => _manualMap.Match(key, season, gameMode),
            MapKeyScope.SeasonOnly => _manualMap.Match(key, season, null),
            _ => _manualMap.Match(key, null, GameModeNames.OneVsOne)
        };

        var resolution = candidates.Count > 0
            ? FromEvents(candidates, manual)
            : WithoutEvents(scope, key, season, manual);

        _cache[cacheKey] = resolution;
        return resolution;
    }

    private IReadOnlyList<EventMapGroup> Candidates(MapKeyScope scope, string key, int? season, int? gameMode)
    {
        switch (scope)
        {
            case MapKeyScope.SeasonAndMode:
                return season != null && _bySeasonAndKey.TryGetValue((key, season.Value), out var bySeasonAndMode)
                    ? bySeasonAndMode.Where(g => g.GameMode == gameMode).ToList()
                    : Array.Empty<EventMapGroup>();
            case MapKeyScope.SeasonOnly:
                return season != null && _bySeasonAndKey.TryGetValue((key, season.Value), out var bySeason)
                    ? bySeason
                    : Array.Empty<EventMapGroup>();
            default:
                return _byKey.TryGetValue(key, out var all)
                    ? all.Where(g => g.GameMode == GameModeNames.OneVsOne).ToList()
                    : Array.Empty<EventMapGroup>();
        }
    }

    private MapKeyResolution FromEvents(IReadOnlyList<EventMapGroup> candidates, IReadOnlyList<ManualMapEntry> manual)
    {
        var identities = GroupByIdentity(candidates);
        var candidateLabels = CandidateLabels(candidates);

        // A manual entry decides the id and the name; the events only give the file.
        if (manual.Count > 0)
        {
            var entry = manual.OrderByDescending(e => e.Games).First();
            var identity = identities.FirstOrDefault(i => i.Any(g =>
                (entry.MapId != null && g.MapId == entry.MapId) || (entry.MapName != null && g.MapName == entry.MapName))) ?? identities[0];
            var eventName = LatestName(identity);
            var notes = new List<string> { $"Manual entry for seasons {entry.SeasonsText}." };
            if (eventName != null && eventName != entry.MapName)
            {
                notes.Add($"Events store '{eventName}'.");
            }

            var manualAmbiguous = CountManualIdentities(manual) > 1;
            if (manualAmbiguous)
            {
                notes.Add("Several manual entries match; the most played was chosen.");
            }

            return new MapKeyResolution(
                manualAmbiguous ? MapKeyStatus.AmbiguousMerged : MapKeyStatus.ResolvedManual,
                MostPlayedFile(identity),
                entry.MapId,
                entry.MapName,
                candidateLabels,
                "manual-map",
                string.Join(" ", notes));
        }

        var chosen = identities[0];
        var file = MostPlayedFile(chosen);
        var mapId = LatestId(chosen);
        var mapName = LatestName(chosen);
        var source = "events";
        var byCatalog = false;

        if (mapId == null && MapDataId(file) is { } dataId)
        {
            mapId = dataId;
            source += "+MapData";
            byCatalog = true;
        }

        if (mapName == null && MmName(mapId) is { } mmName)
        {
            mapName = mmName;
            source += "+MM Maps";
            byCatalog = true;
        }

        var ambiguous = identities.Count > 1;
        var fileCount = candidates.Select(c => c.File).Distinct(StringComparer.Ordinal).Count();
        var note = ambiguous
            ? $"{identities.Count} different maps share this key; the most played was chosen."
            : fileCount > 1 ? $"{fileCount} files share this key; the most played was chosen." : string.Empty;

        return new MapKeyResolution(
            ambiguous ? MapKeyStatus.AmbiguousMerged : byCatalog ? MapKeyStatus.ResolvedByCatalog : MapKeyStatus.Resolved,
            file,
            mapId,
            mapName,
            candidateLabels,
            source,
            note);
    }

    private MapKeyResolution WithoutEvents(MapKeyScope scope, string key, int? season, IReadOnlyList<ManualMapEntry> manual)
    {
        var seasonHasEvents = season != null && HasEvents(season.Value);

        if (manual.Count > 0)
        {
            var entry = manual.OrderByDescending(e => e.Games).First();
            var ambiguous = CountManualIdentities(manual) > 1;
            var status = ambiguous
                ? MapKeyStatus.AmbiguousMerged
                : scope == MapKeyScope.AllSeasonsOneVsOne || !seasonHasEvents ? MapKeyStatus.LegacyKey : MapKeyStatus.ResolvedManual;

            return new MapKeyResolution(
                status,
                key,
                entry.MapId,
                entry.MapName,
                manual.Select(e => $"manual: {e.MapName ?? "(no name)"} (id {e.MapId?.ToString() ?? "null"}, seasons {e.SeasonsText})").ToList(),
                "manual-map",
                ambiguous ? "Several manual entries match; the most played was chosen." : $"No event for this key; manual entry for seasons {entry.SeasonsText}.");
        }

        if (scope == MapKeyScope.AllSeasonsOneVsOne)
        {
            return new MapKeyResolution(MapKeyStatus.Unresolved, key, null, null, Array.Empty<string>(), "none", "No 1v1 event and no manual entry for this key.");
        }

        if (!seasonHasEvents)
        {
            return new MapKeyResolution(MapKeyStatus.LegacyKey, key, null, null, Array.Empty<string>(), "none", "No event in this season and no manual entry; the key is kept.");
        }

        // A key in a season with events that no event reproduces: an older version of the mangling.
        var fallback = _metadataResolver.Resolve(key);
        if (fallback.Status == MapMetadataResolutionStatus.Resolved && (fallback.MapId != null || fallback.MapName != null))
        {
            return new MapKeyResolution(MapKeyStatus.ResolvedByCatalog, key, fallback.MapId, fallback.MapName, fallback.SourceMaps.ToList(), $"resolver:{fallback.Confidence}", fallback.Notes);
        }

        return new MapKeyResolution(MapKeyStatus.Unresolved, key, null, null, fallback.SourceMaps.ToList(), "none", fallback.Notes);
    }

    // Two candidates are the same map when they share a file, a MapId or a MapName. A file always belongs to one map,
    // so a map-only event group joins the groups of the same file that carry an id or a name.
    public static List<List<EventMapGroup>> GroupByIdentity(IReadOnlyList<EventMapGroup> groups)
    {
        var parent = Enumerable.Range(0, groups.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        void Union(int a, int b) => parent[Find(a)] = Find(b);

        var byId = new Dictionary<int, int>();
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        var byFile = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (group.File != null)
            {
                LinkOrAdd(byFile, group.File, i);
            }

            if (group.MapId != null)
            {
                LinkOrAdd(byId, group.MapId.Value, i);
            }

            if (!string.IsNullOrEmpty(group.MapName))
            {
                LinkOrAdd(byName, group.MapName, i);
            }
        }

        return Enumerable.Range(0, groups.Count)
            .GroupBy(Find)
            .Select(g => g.Select(i => groups[i]).ToList())
            .OrderByDescending(g => g.Sum(x => x.Count))
            .ThenBy(g => g.Min(x => x.File), StringComparer.Ordinal)
            .ToList();

        void LinkOrAdd<TKey>(Dictionary<TKey, int> index, TKey key, int i)
        {
            if (index.TryGetValue(key, out var first))
            {
                Union(i, first);
            }
            else
            {
                index[key] = i;
            }
        }
    }

    private static int CountManualIdentities(IReadOnlyList<ManualMapEntry> entries)
    {
        var asGroups = entries
            .Select((e, i) => new EventMapGroup("manual:" + i, e.MapId, e.MapName, 0, 0, e.Games, MongoDB.Bson.ObjectId.Empty))
            .ToList();
        return GroupByIdentity(asGroups).Count;
    }

    private static string MostPlayedFile(IEnumerable<EventMapGroup> identity)
    {
        return identity
            .GroupBy(g => g.File, StringComparer.Ordinal)
            .OrderByDescending(g => g.Sum(x => x.Count))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .First()
            .Key;
    }

    private static int? LatestId(IEnumerable<EventMapGroup> identity)
    {
        return identity.Where(g => g.MapId != null).OrderByDescending(g => g.LastId).Select(g => g.MapId).FirstOrDefault();
    }

    private static string LatestName(IEnumerable<EventMapGroup> identity)
    {
        return identity.Where(g => !string.IsNullOrEmpty(g.MapName)).OrderByDescending(g => g.LastId).Select(g => g.MapName).FirstOrDefault();
    }

    private static IReadOnlyList<string> CandidateLabels(IEnumerable<EventMapGroup> candidates)
    {
        return candidates
            .GroupBy(c => (c.File, c.MapId, c.MapName))
            .OrderByDescending(g => g.Sum(x => x.Count))
            .ThenBy(g => g.Key.File, StringComparer.Ordinal)
            .Select(g => $"{g.Key.File} (id {g.Key.MapId?.ToString() ?? "null"}, {g.Key.MapName ?? "no name"}, {g.Sum(x => x.Count)} games)")
            .ToList();
    }

    private static void Add<TKey>(Dictionary<TKey, List<EventMapGroup>> index, TKey key, EventMapGroup group)
    {
        if (!index.TryGetValue(key, out var list))
        {
            list = new List<EventMapGroup>();
            index[key] = list;
        }

        list.Add(group);
    }
}
