using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Tools.MapMetadataBackfill;

public static class CsvReportWriter
{
    public static async Task WriteAsync(string path, IReadOnlyCollection<MapMetadataBackfillReportRow> rows)
    {
        await using var stream = File.Create(path);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));

        await writer.WriteLineAsync(string.Join(",", Header.Select(Escape)));
        foreach (var row in rows)
        {
            var fields = new[]
            {
                row.Action,
                row.TargetMap,
                row.MatchCount.ToString(),
                string.Join("|", row.Seasons.OrderBy(s => s)),
                string.Join("|", row.GameModes.OrderBy(g => g)),
                row.ProposedMapName,
                row.ProposedMapId?.ToString() ?? string.Empty,
                row.Confidence,
                row.Status,
                row.ModifiedCount.ToString(),
                string.Join("|", row.SampleIds),
                string.Join("|", row.SourceMaps),
                row.Notes
            };

            await writer.WriteLineAsync(string.Join(",", fields.Select(Escape)));
        }
    }

    private static readonly string[] Header =
    {
        "action",
        "target_map",
        "match_count",
        "seasons",
        "game_modes",
        "proposed_map_name",
        "proposed_map_id",
        "confidence",
        "status",
        "modified_count",
        "sample_ids",
        "source_maps",
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

public sealed record MapMetadataBackfillReportRow(
    string TargetMap,
    long MatchCount,
    IReadOnlyCollection<int> Seasons,
    IReadOnlyCollection<int> GameModes,
    string ProposedMapName,
    int? ProposedMapId,
    string Confidence,
    string Status,
    string Action,
    long ModifiedCount,
    IReadOnlyCollection<string> SampleIds,
    IReadOnlyCollection<string> SourceMaps,
    string Notes);
