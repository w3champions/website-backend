using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;

namespace W3ChampionsStatisticService.Tools.MapKeyBackfill;

public sealed record MapKeyTransformResult(BsonDocument Document, bool Changed);

// Event fields joined to a Matchup document.
public sealed record JoinedEventMap(string Path, int? MapId, string MapName);

// Pure BsonDocument rewrites. Only Map, MapId and MapName of map entries change; entries that end up with the
// same Map are merged (counts and wins/losses summed); every other element is kept as it is.
public static class MapKeyTransforms
{
    public const string Matchup = "Matchup";
    public const string MapsPerSeason = "MapsPerSeason";
    public const string PlayerHeroStats = "PlayerHeroStats";
    public const string PlayerRaceOnMapVersusRaceRatio = "PlayerRaceOnMapVersusRaceRatio";
    public const string OverallRaceAndWinStat = "OverallRaceAndWinStat";

    public const string AllSeasonsId = "-1";
    private const string Overall = "Overall";

    private static readonly HashSet<string> SummedFields = new(StringComparer.Ordinal) { "Count", "Wins", "Losses" };
    private static readonly HashSet<string> RaceListFields = new(StringComparer.Ordinal) { "WinLosses", "Ratio" };

    public static MapKeyTransformResult TransformPlayerHeroStats(BsonDocument document, MapKeyResolver resolver, MapKeyReport report)
    {
        var result = document.DeepClone().AsBsonDocument;
        var season = SeasonOf(result);
        var record = Recorder(report, PlayerHeroStats, result["_id"], season, null);
        var changed = false;

        foreach (var hero in Documents(result.GetValue("HeroStatsItemList", BsonNull.Value)))
        {
            foreach (var race in Documents(hero.GetValue("Stats", BsonNull.Value)))
            {
                changed |= MigrateField(race, "WinLossesOnMap", key => resolver.Resolve(MapKeyScope.SeasonOnly, key, season, null), record, legacyKeyInMapName: false, sort: false);
            }
        }

        return new MapKeyTransformResult(result, changed);
    }

    public static MapKeyTransformResult TransformPlayerRaceOnMapVersusRaceRatio(BsonDocument document, MapKeyResolver resolver, MapKeyReport report)
    {
        var result = document.DeepClone().AsBsonDocument;
        var season = SeasonOf(result);
        var record = Recorder(report, PlayerRaceOnMapVersusRaceRatio, result["_id"], season, null);
        MapKeyResolution Resolve(string key) => resolver.Resolve(MapKeyScope.SeasonOnly, key, season, null);
        var changed = false;

        if (result.GetValue("RaceWinsOnMapByPatch", BsonNull.Value) is BsonDocument byPatch)
        {
            foreach (var patch in byPatch.Elements.ToList())
            {
                foreach (var race in Documents(patch.Value))
                {
                    changed |= MigrateField(race, "WinLossesOnMap", Resolve, record, legacyKeyInMapName: false, sort: false);
                }
            }
        }

        foreach (var race in Documents(result.GetValue("RaceWinsOnMap", BsonNull.Value)))
        {
            changed |= MigrateField(race, "WinLossesOnMap", Resolve, record, legacyKeyInMapName: false, sort: false);
        }

        return new MapKeyTransformResult(result, changed);
    }

    public static MapKeyTransformResult TransformOverallRaceAndWinStat(BsonDocument document, MapKeyResolver resolver, MapKeyReport report)
    {
        var result = document.DeepClone().AsBsonDocument;
        var record = Recorder(report, OverallRaceAndWinStat, result["_id"], null, GameModeNames.OneVsOne);
        MapKeyResolution Resolve(string key) => resolver.Resolve(MapKeyScope.AllSeasonsOneVsOne, key, null, GameModeNames.OneVsOne);
        var changed = false;

        if (result.GetValue("PatchToStatsPerModes", BsonNull.Value) is BsonDocument byPatch)
        {
            foreach (var patch in byPatch.Names.ToList())
            {
                changed |= MigrateField(byPatch, patch, Resolve, record, legacyKeyInMapName: true, sort: false);
            }
        }

        changed |= MigrateField(result, "StatsPerModes", Resolve, record, legacyKeyInMapName: true, sort: false);
        return new MapKeyTransformResult(result, changed);
    }

    // A season document (Season >= 0). Mode buckets are migrated; the all-modes bucket is rebuilt as their sum
    // when its stored total equals that sum, and migrated in place otherwise.
    public static MapKeyTransformResult TransformMapsPerSeasonSeason(BsonDocument document, MapKeyResolver resolver, MapKeyReport report)
    {
        var result = document.DeepClone().AsBsonDocument;
        var season = SeasonOf(result);
        var id = result["_id"];
        var changed = false;
        var buckets = Documents(result.GetValue("MatchesOnMapPerModes", BsonNull.Value)).ToList();

        foreach (var bucket in buckets.Where(b => GameModeOf(b) != GameModeNames.Undefined))
        {
            var mode = GameModeOf(bucket);
            changed |= MigrateField(
                bucket,
                "Maps",
                key => resolver.Resolve(MapKeyScope.SeasonAndMode, key, season, mode),
                Recorder(report, MapsPerSeason, id, season, mode),
                legacyKeyInMapName: false,
                sort: true);
        }

        var allModes = buckets.FirstOrDefault(b => GameModeOf(b) == GameModeNames.Undefined);
        if (allModes == null)
        {
            return new MapKeyTransformResult(result, changed);
        }

        var modeEntries = buckets.Where(b => GameModeOf(b) != GameModeNames.Undefined).SelectMany(b => Documents(b.GetValue("Maps", BsonNull.Value))).ToList();
        var storedTotal = TotalCount(Documents(allModes.GetValue("Maps", BsonNull.Value)));
        var sumTotal = TotalCount(modeEntries);
        if (storedTotal == sumTotal)
        {
            var rebuilt = SumEntries(modeEntries.Select(e => (e, 0)));
            if (!SameEntries(allModes.GetValue("Maps", BsonNull.Value), rebuilt))
            {
                allModes["Maps"] = rebuilt;
                changed = true;
            }

            report?.RecordRebuild(MapsPerSeason, id, season ?? 0, GameModeNames.Undefined, true, storedTotal, sumTotal, "the mode buckets");
        }
        else
        {
            changed |= MigrateField(
                allModes,
                "Maps",
                key => resolver.Resolve(MapKeyScope.SeasonOnly, key, season, null),
                Recorder(report, MapsPerSeason, id, season, GameModeNames.Undefined),
                legacyKeyInMapName: false,
                sort: true);
            report?.RecordRebuild(MapsPerSeason, id, season ?? 0, GameModeNames.Undefined, false, storedTotal, sumTotal, "the mode buckets");
        }

        return new MapKeyTransformResult(result, changed);
    }

    // The all-seasons document (_id "-1") is rebuilt as the sum of the migrated season documents, per mode bucket.
    // The season documents are the reference: a bucket whose stored total differs from that sum is rebuilt anyway,
    // and both totals are reported. A mode the seasons have but the document lacks gets a new bucket.
    public static MapKeyTransformResult RebuildMapsPerSeasonAllSeasons(BsonDocument allSeasons, IReadOnlyList<BsonDocument> migratedSeasons, MapKeyReport report)
    {
        var result = allSeasons.DeepClone().AsBsonDocument;
        var id = result["_id"];
        var changed = false;
        if (result.GetValue("MatchesOnMapPerModes", BsonNull.Value) is not BsonArray buckets)
        {
            buckets = new BsonArray();
            result["MatchesOnMapPerModes"] = buckets;
        }

        var seasonBuckets = migratedSeasons
            .SelectMany(doc => Documents(doc.GetValue("MatchesOnMapPerModes", BsonNull.Value)).Select(bucket => (Season: SeasonOf(doc) ?? 0, Bucket: bucket)))
            .ToList();

        var modes = Documents(buckets).Select(GameModeOf)
            .Concat(seasonBuckets.Select(b => GameModeOf(b.Bucket)).Distinct().OrderBy(m => m))
            .Distinct()
            .ToList();

        foreach (var mode in modes)
        {
            var entries = seasonBuckets
                .Where(b => GameModeOf(b.Bucket) == mode)
                .SelectMany(b => Documents(b.Bucket.GetValue("Maps", BsonNull.Value)).Select(e => (e, b.Season)))
                .ToList();
            var bucket = Documents(buckets).FirstOrDefault(b => GameModeOf(b) == mode);
            var storedTotal = bucket == null ? 0 : TotalCount(Documents(bucket.GetValue("Maps", BsonNull.Value)));
            var sumTotal = TotalCount(entries.Select(e => e.e));

            var rebuilt = SumEntries(entries);
            if (bucket == null)
            {
                buckets.Add(new BsonDocument { ["GameMode"] = mode, ["Maps"] = rebuilt });
                changed = true;
            }
            else if (!SameEntries(bucket.GetValue("Maps", BsonNull.Value), rebuilt))
            {
                bucket["Maps"] = rebuilt;
                changed = true;
            }

            report?.RecordRebuild(MapsPerSeason, id, -1, mode, true, storedTotal, sumTotal, "the season documents");
        }

        return new MapKeyTransformResult(result, changed);
    }

    // Returns the $set fields for a Matchup document, or null when it doesn't change. MapId and MapName are only
    // filled where missing: the stored ones were copied from the event when the match was saved.
    public static BsonDocument MatchupUpdate(BsonDocument document, JoinedEventMap joined, MapKeyResolver resolver, MapKeyReport report)
    {
        if (document.GetValue("Map", BsonNull.Value) is not BsonString mapValue || mapValue.Value.Length == 0)
        {
            return null;
        }

        var key = mapValue.Value;
        var id = document["_id"];
        if (resolver.IsKnownFile(key))
        {
            report?.Record(Matchup, id, key, null, null, MapKeyResolution.Migrated);
            return null;
        }

        var season = ReadNullableInt(document, "Season");
        var mode = ReadNullableInt(document, "GameMode");
        var storedId = ReadNullableInt(document, "MapId");
        var storedName = document.GetValue("MapName", BsonNull.Value) is BsonString n && n.Value.Length > 0 ? n.Value : null;

        MapKeyResolution resolution;
        if (joined?.Path != null)
        {
            var file = MapFileName.Of(joined.Path);
            var mapId = storedId ?? joined.MapId;
            var mapName = storedName ?? joined.MapName;
            var source = "event";
            var manual = resolver.BestManualEntry(key, season, mode);
            if (mapId == null && (manual?.MapId ?? resolver.MapDataId(file)) is { } filledId)
            {
                mapId = filledId;
                source += manual?.MapId != null ? "+manual-map" : "+MapData";
            }

            if (mapName == null && (manual?.MapName ?? resolver.MmName(mapId)) is { } filledName)
            {
                mapName = filledName;
                source += manual?.MapName != null ? "+manual-map" : "+MM Maps";
            }

            var status = source == "event" ? MapKeyStatus.Resolved : source.Contains("manual-map") ? MapKeyStatus.ResolvedManual : MapKeyStatus.ResolvedByCatalog;
            resolution = new MapKeyResolution(status, file, mapId, mapName, Array.Empty<string>(), source, "Joined to its MatchFinishedEvent.");
        }
        else
        {
            resolution = resolver.Resolve(MapKeyScope.SeasonAndMode, key, season, mode);
        }

        report?.Record(Matchup, id, key, season, mode, resolution);
        if (!resolution.ChangesEntry)
        {
            return null;
        }

        var set = new BsonDocument();
        if (resolution.Map != key)
        {
            set["Map"] = resolution.Map;
        }

        if (storedId == null && resolution.MapId != null)
        {
            set["MapId"] = resolution.MapId.Value;
        }

        if (storedName == null && resolution.MapName != null)
        {
            set["MapName"] = resolution.MapName;
        }

        return set.ElementCount > 0 ? set : null;
    }

    private static bool MigrateField(
        BsonDocument container,
        string field,
        Func<string, MapKeyResolution> resolve,
        Action<string, MapKeyResolution, int, int> record,
        bool legacyKeyInMapName,
        bool sort)
    {
        if (container.GetValue(field, BsonNull.Value) is not BsonArray entries)
        {
            return false;
        }

        var migrated = MigrateEntries(entries, resolve, record, legacyKeyInMapName);
        if (migrated.Equals(entries))
        {
            return false;
        }

        // Sorted only when something changed, so documents already in the new shape aren't rewritten for the order.
        if (sort)
        {
            migrated = new BsonArray(migrated.OrderBy(e => e is BsonDocument d && d.GetValue("Map", BsonNull.Value) is BsonString s ? s.Value : string.Empty, StringComparer.Ordinal));
        }

        container[field] = migrated;
        return true;
    }

    // legacyKeyInMapName: OverallRaceAndWinStat entries written before the switch have no Map and keep the key in MapName.
    public static BsonArray MigrateEntries(
        BsonArray entries,
        Func<string, MapKeyResolution> resolve,
        Action<string, MapKeyResolution, int, int> record,
        bool legacyKeyInMapName)
    {
        var output = new List<(BsonValue Value, bool WasMigrated, string OriginKey, MapKeyResolution Origin)>();
        var indexByMap = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var value in entries)
        {
            if (value is not BsonDocument entry)
            {
                output.Add((value, false, null, null));
                continue;
            }

            var key = entry.GetValue("Map", BsonNull.Value) is BsonString map ? map.Value : null;
            var keyInMapName = false;
            if (key == null && legacyKeyInMapName && entry.GetValue("MapName", BsonNull.Value) is BsonString name)
            {
                key = name.Value;
                keyInMapName = true;
            }

            if (key == null)
            {
                output.Add((entry, false, null, null));
                continue;
            }

            BsonDocument target;
            bool wasMigrated;
            MapKeyResolution resolution = null;
            if (key == Overall)
            {
                target = keyInMapName ? Rewrite(entry, Overall, null, Overall, keyInMapName) : entry;
                wasMigrated = true;
            }
            else
            {
                resolution = resolve(key);
                wasMigrated = resolution.Status == MapKeyStatus.Migrated;
                target = resolution.ChangesEntry ? Rewrite(entry, resolution.Map, resolution.MapId, resolution.MapName, keyInMapName) : entry;
            }

            var isLegacy = resolution != null && resolution.Status != MapKeyStatus.Migrated;
            var merged = false;
            var targetMap = target.GetValue("Map", BsonNull.Value) is BsonString t ? t.Value : null;
            if (targetMap != null && indexByMap.TryGetValue(targetMap, out var index))
            {
                var existing = output[index];
                output[index] = (
                    Merge(existing.Value.AsBsonDocument, existing.WasMigrated, target, wasMigrated),
                    existing.WasMigrated || wasMigrated,
                    existing.OriginKey ?? (isLegacy ? key : null),
                    existing.Origin ?? (isLegacy ? resolution : null));

                // A merge is reported on the legacy key: this entry's, or the one that produced the entry merged into.
                if (isLegacy)
                {
                    merged = true;
                }
                else if (existing.Origin != null)
                {
                    record?.Invoke(existing.OriginKey, existing.Origin, 0, 1);
                }
            }
            else
            {
                if (targetMap != null)
                {
                    indexByMap[targetMap] = output.Count;
                }

                output.Add((target, wasMigrated, isLegacy ? key : null, isLegacy ? resolution : null));
            }

            if (resolution != null)
            {
                record?.Invoke(key, resolution, 1, merged ? 1 : 0);
            }
        }

        return new BsonArray(output.Select(o => o.Value));
    }

    private static BsonDocument Rewrite(BsonDocument entry, string map, int? mapId, string mapName, bool keyInMapName)
    {
        var storedId = entry.GetValue("MapId", BsonNull.Value);
        var storedName = keyInMapName ? BsonNull.Value : entry.GetValue("MapName", BsonNull.Value);
        var result = new BsonDocument { ["Map"] = map };

        BsonValue id = mapId.HasValue ? new BsonInt32(mapId.Value) : storedId;
        if (!id.IsBsonNull || entry.Contains("MapId"))
        {
            result["MapId"] = id;
        }

        BsonValue name = mapName != null ? new BsonString(mapName) : storedName;
        if (!name.IsBsonNull || entry.Contains("MapName"))
        {
            result["MapName"] = name;
        }

        foreach (var element in entry.Elements)
        {
            if (element.Name is not ("Map" or "MapId" or "MapName"))
            {
                result[element.Name] = element.Value.DeepClone();
            }
        }

        return result;
    }

    private static BsonDocument Merge(BsonDocument existing, bool existingMigrated, BsonDocument incoming, bool incomingMigrated)
    {
        var result = MergeCounters(existing, incoming);

        // Keep the id and name of an entry already in the new shape: the handlers wrote them from newer events.
        var preferIncoming = incomingMigrated && !existingMigrated;
        foreach (var field in new[] { "MapId", "MapName" })
        {
            var current = result.GetValue(field, BsonNull.Value);
            var other = incoming.GetValue(field, BsonNull.Value);
            if (!other.IsBsonNull && (current.IsBsonNull || preferIncoming))
            {
                result[field] = other.DeepClone();
            }
        }

        return result;
    }

    private static BsonDocument MergeCounters(BsonDocument existing, BsonDocument incoming)
    {
        var result = existing.DeepClone().AsBsonDocument;
        foreach (var element in incoming.Elements)
        {
            if (SummedFields.Contains(element.Name) && element.Value.IsNumeric)
            {
                result[element.Name] = Sum(result.GetValue(element.Name, BsonNull.Value), element.Value);
            }
            else if (RaceListFields.Contains(element.Name) && element.Value is BsonArray incomingList)
            {
                result[element.Name] = result.GetValue(element.Name, BsonNull.Value) is BsonArray existingList
                    ? MergeRaceList(existingList, incomingList)
                    : incomingList.DeepClone();
            }
        }

        return result;
    }

    // Lists of { Race, ... } items, merged by Race: WinLosses ({ Race, Wins, Losses }) and Ratio ({ Race, WinLosses }).
    private static BsonArray MergeRaceList(BsonArray existing, BsonArray incoming)
    {
        var result = existing.DeepClone().AsBsonArray;
        foreach (var item in incoming)
        {
            if (item is not BsonDocument incomingItem)
            {
                continue;
            }

            var race = incomingItem.GetValue("Race", BsonNull.Value);
            var index = -1;
            for (var i = 0; i < result.Count; i++)
            {
                if (result[i] is BsonDocument d && d.GetValue("Race", BsonNull.Value).Equals(race))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                result.Add(incomingItem.DeepClone());
            }
            else
            {
                result[index] = MergeCounters(result[index].AsBsonDocument, incomingItem);
            }
        }

        return result;
    }

    private static BsonValue Sum(BsonValue a, BsonValue b)
    {
        var total = (a.IsNumeric ? a.ToInt64() : 0) + b.ToInt64();
        var bothInt32 = (a.IsBsonNull || a.IsInt32) && b.IsInt32;
        return bothInt32 && total <= int.MaxValue && total >= int.MinValue ? new BsonInt32((int)total) : new BsonInt64(total);
    }

    // Sums MapsPerSeason entries per (Map, MapId), or per (Map, MapName) without an id. Id and name come from the
    // entry with the highest priority (the latest season) that has them.
    private static BsonArray SumEntries(IEnumerable<(BsonDocument Entry, int Priority)> entries)
    {
        var groups = new Dictionary<string, List<(BsonDocument Entry, int Priority)>>(StringComparer.Ordinal);
        foreach (var item in entries)
        {
            var map = item.Entry.GetValue("Map", BsonNull.Value);
            var mapId = item.Entry.GetValue("MapId", BsonNull.Value);
            var mapName = item.Entry.GetValue("MapName", BsonNull.Value);
            var key = map + "\u0001" + (mapId.IsNumeric ? "id:" + mapId.ToInt32() : "name:" + (mapName.IsString ? mapName.AsString : string.Empty));
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<(BsonDocument, int)>();
                groups[key] = list;
            }

            list.Add(item);
        }

        var result = groups.Values.Select(list =>
        {
            var latestFirst = list.OrderByDescending(i => i.Priority).Select(i => i.Entry).ToList();
            var entry = new BsonDocument { ["Map"] = latestFirst[0].GetValue("Map", BsonNull.Value) };
            if (latestFirst.Any(e => e.Contains("MapId")))
            {
                entry["MapId"] = latestFirst.Select(e => e.GetValue("MapId", BsonNull.Value)).FirstOrDefault(v => !v.IsBsonNull) ?? BsonNull.Value;
            }

            if (latestFirst.Any(e => e.Contains("MapName")))
            {
                entry["MapName"] = latestFirst.Select(e => e.GetValue("MapName", BsonNull.Value)).FirstOrDefault(v => !v.IsBsonNull) ?? BsonNull.Value;
            }

            entry["Count"] = list.Aggregate((BsonValue)BsonNull.Value, (sum, i) => Sum(sum, i.Entry.GetValue("Count", new BsonInt32(0))));
            return entry;
        });

        return new BsonArray(result.OrderBy(EntrySortKey, StringComparer.Ordinal));
    }

    private static bool SameEntries(BsonValue stored, BsonArray rebuilt)
    {
        if (stored is not BsonArray storedList || storedList.Count != rebuilt.Count)
        {
            return false;
        }

        var a = storedList.OfType<BsonDocument>().OrderBy(EntrySortKey, StringComparer.Ordinal).ToList();
        var b = rebuilt.OfType<BsonDocument>().OrderBy(EntrySortKey, StringComparer.Ordinal).ToList();
        return a.Count == storedList.Count && a.SequenceEqual(b);
    }

    private static string EntrySortKey(BsonDocument entry)
    {
        return entry.GetValue("Map", BsonNull.Value) + "\u0001" + entry.GetValue("MapId", BsonNull.Value) + "\u0001" + entry.GetValue("MapName", BsonNull.Value);
    }

    private static long TotalCount(IEnumerable<BsonDocument> entries)
    {
        return entries.Sum(e => e.GetValue("Count", BsonNull.Value) is { IsNumeric: true } count ? count.ToInt64() : 0);
    }

    private static Action<string, MapKeyResolution, int, int> Recorder(MapKeyReport report, string collection, BsonValue id, int? season, int? gameMode)
    {
        return report == null ? null : (key, resolution, entries, merged) => report.Record(collection, id, key, season, gameMode, resolution, entries, merged);
    }

    private static IEnumerable<BsonDocument> Documents(BsonValue value)
    {
        return value is BsonArray array ? array.OfType<BsonDocument>() : Enumerable.Empty<BsonDocument>();
    }

    private static int GameModeOf(BsonDocument bucket) => ReadNullableInt(bucket, "GameMode") ?? GameModeNames.Undefined;

    // Season field, or the "{season}_..." prefix of the id.
    public static int? SeasonOf(BsonDocument document)
    {
        if (ReadNullableInt(document, "Season") is { } season)
        {
            return season;
        }

        var id = document.GetValue("_id", BsonNull.Value);
        if (id.IsString)
        {
            var prefix = id.AsString.Split('_')[0];
            if (int.TryParse(prefix, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static int? ReadNullableInt(BsonDocument document, string name)
    {
        var value = document.GetValue(name, BsonNull.Value);
        return value.IsNumeric ? value.ToInt32() : null;
    }
}
