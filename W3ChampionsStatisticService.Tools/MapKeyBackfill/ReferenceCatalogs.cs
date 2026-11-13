using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MongoDB.Bson;

namespace W3ChampionsStatisticService.Tools.MapKeyBackfill;

// Loaders for mongoexport files: one JSON document per line, or a single array (--jsonArray).
public static class ReferenceCatalogs
{
    // UpdateService MapData: map file name -> MapId. The same file may be listed more than once (several paths);
    // a file listed under different ids is dropped and reported, since it can't tell which map it is.
    public static IReadOnlyDictionary<string, int> LoadMapData(string path, Action<string> warn)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var conflicting = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(path))
        {
            return ids;
        }

        foreach (var doc in ReadExport(path))
        {
            var file = MapFileName.Of(doc.GetValue("FilePath", BsonNull.Value) is BsonString s ? s.Value : null);
            var mapId = doc.GetValue("MapId", BsonNull.Value);
            if (string.IsNullOrWhiteSpace(file) || !mapId.IsNumeric)
            {
                continue;
            }

            var id = mapId.ToInt32();
            if (ids.TryGetValue(file, out var existing) && existing != id)
            {
                conflicting.Add(file);
            }

            ids.TryAdd(file, id);
        }

        foreach (var file in conflicting)
        {
            ids.Remove(file);
            warn($"MapData lists '{file}' under more than one MapId; it is ignored.");
        }

        return ids;
    }

    // matchmaking-service Maps: _id -> name.
    public static IReadOnlyDictionary<int, string> LoadMmMaps(string path)
    {
        var names = new Dictionary<int, string>();
        if (string.IsNullOrWhiteSpace(path))
        {
            return names;
        }

        foreach (var doc in ReadExport(path))
        {
            var id = doc.GetValue("_id", BsonNull.Value);
            var name = doc.GetValue("name", BsonNull.Value);
            if (id.IsNumeric && name is BsonString s && !string.IsNullOrWhiteSpace(s.Value))
            {
                names[id.ToInt32()] = s.Value;
            }
        }

        return names;
    }

    public static IEnumerable<BsonDocument> ReadExport(string path)
    {
        var text = File.ReadAllText(path);
        if (text.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            return BsonDocument.Parse("{\"documents\":" + text + "}")["documents"].AsBsonArray.Select(v => v.AsBsonDocument).ToList();
        }

        return text
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(BsonDocument.Parse)
            .ToList();
    }
}
