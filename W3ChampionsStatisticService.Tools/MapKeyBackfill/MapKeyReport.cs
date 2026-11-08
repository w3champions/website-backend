using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MongoDB.Bson;

namespace W3ChampionsStatisticService.Tools.MapKeyBackfill;

public sealed class MapKeyReportRow
{
    public string Collection { get; init; }
    public string LegacyKey { get; init; }
    public int? Season { get; init; }
    public int? GameMode { get; init; }
    public MapKeyStatus Status { get; init; }
    public string ChosenFile { get; init; }
    public int? MapId { get; init; }
    public string MapName { get; init; }
    public IReadOnlyList<string> CandidateFiles { get; init; }
    public string Source { get; init; }
    public string Notes { get; set; }
    public long Documents { get; set; }
    public long Entries { get; set; }
    public long EntriesMerged { get; set; }
    internal BsonValue LastDocumentId { get; set; }
}

public sealed class MapKeyCollectionTotals
{
    public long DocumentsRead { get; set; }
    public long DocumentsChanged { get; set; }
    public long DocumentsWritten { get; set; }
    public long EntriesAlreadyMigrated { get; set; }
}

public sealed class MapKeyReport
{
    private readonly Dictionary<(string, string, int?, int?, MapKeyStatus, string, int?, string), MapKeyReportRow> _rows = new();
    private readonly Dictionary<string, MapKeyCollectionTotals> _totals = new(StringComparer.Ordinal);

    public IReadOnlyList<MapKeyReportRow> Rows => _rows.Values
        .OrderBy(r => r.Collection, StringComparer.Ordinal)
        .ThenBy(r => r.Season ?? int.MinValue)
        .ThenBy(r => r.GameMode ?? int.MinValue)
        .ThenBy(r => r.LegacyKey, StringComparer.Ordinal)
        .ThenBy(r => r.ChosenFile, StringComparer.Ordinal)
        .ToList();

    public IReadOnlyDictionary<string, MapKeyCollectionTotals> Totals => _totals;

    public MapKeyCollectionTotals TotalsFor(string collection)
    {
        if (!_totals.TryGetValue(collection, out var totals))
        {
            totals = new MapKeyCollectionTotals();
            _totals[collection] = totals;
        }

        return totals;
    }

    public void Record(string collection, BsonValue documentId, string legacyKey, int? season, int? gameMode, MapKeyResolution resolution, int entries = 1, int merged = 0)
    {
        if (resolution.Status == MapKeyStatus.Migrated)
        {
            TotalsFor(collection).EntriesAlreadyMigrated += entries;
            return;
        }

        var chosenFile = resolution.Map == legacyKey ? null : resolution.Map;
        var key = (collection, legacyKey, season, gameMode, resolution.Status, chosenFile, resolution.MapId, resolution.MapName);
        if (!_rows.TryGetValue(key, out var row))
        {
            row = new MapKeyReportRow
            {
                Collection = collection,
                LegacyKey = legacyKey,
                Season = season,
                GameMode = gameMode,
                Status = resolution.Status,
                ChosenFile = chosenFile,
                MapId = resolution.MapId,
                MapName = resolution.MapName,
                CandidateFiles = resolution.CandidateFiles,
                Source = resolution.Source,
                Notes = resolution.Notes
            };
            _rows[key] = row;
        }

        // Documents are processed one after the other, so counting a new document id is enough.
        if (row.LastDocumentId == null || !row.LastDocumentId.Equals(documentId))
        {
            row.Documents++;
            row.LastDocumentId = documentId;
        }

        row.Entries += entries;
        row.EntriesMerged += merged;
    }

    public void RecordRebuild(string collection, BsonValue documentId, int season, int gameMode, bool rebuilt, long oldTotal, long newTotal, string what)
    {
        var resolution = new MapKeyResolution(
            rebuilt ? MapKeyStatus.Rebuilt : MapKeyStatus.NotRebuilt,
            null,
            null,
            null,
            Array.Empty<string>(),
            what,
            rebuilt
                ? $"Rebuilt from {what}; total {newTotal}."
                : $"Not rebuilt: stored total {oldTotal} differs from the sum of {what} ({newTotal}). Left as it is.");
        Record(collection, documentId, null, season, gameMode, resolution);
    }

    public void Write(string path)
    {
        using var writer = new StreamWriter(File.Create(path), new UTF8Encoding(false));
        writer.WriteLine(string.Join(",", Header));
        foreach (var row in Rows)
        {
            var fields = new[]
            {
                row.Collection,
                row.LegacyKey,
                row.Season?.ToString() ?? string.Empty,
                GameModeNames.NameOf(row.GameMode),
                row.Status.ToCsv(),
                row.ChosenFile,
                row.MapId?.ToString() ?? string.Empty,
                row.MapName,
                string.Join(" | ", row.CandidateFiles ?? Array.Empty<string>()),
                row.Source,
                row.Documents.ToString(),
                row.Entries.ToString(),
                row.EntriesMerged.ToString(),
                row.Notes
            };
            writer.WriteLine(string.Join(",", fields.Select(Escape)));
        }
    }

    private static readonly string[] Header =
    {
        "collection",
        "legacy_key",
        "season",
        "game_mode",
        "status",
        "chosen_file",
        "map_id",
        "map_name",
        "candidate_files",
        "source",
        "docs_touched",
        "entries",
        "entries_merged",
        "notes"
    };

    private static string Escape(string value)
    {
        value ??= string.Empty;
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
