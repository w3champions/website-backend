using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;

namespace W3ChampionsStatisticService.Admin.ReplayExport;

public class ReplayArchiveEntry
{
    /// <summary>Path inside the ZIP, or null when the replay could not be fetched.</summary>
    public string File { get; set; }

    /// <summary>"ok", "not-found", "archived", "empty", "timeout" or "failed".</summary>
    public string Status { get; set; }
    public string Error { get; set; }
    public ReplayExportMatch Match { get; set; }
}

public class ReplayArchiveSummary
{
    public int Written { get; set; }
    public int Failed { get; set; }

    /// <summary>Ids of the games whose replay is in the ZIP.</summary>
    public List<string> WrittenMatchIds { get; set; } = [];
}

/// <summary>
/// The top of manifest.json. Buyers can get the ZIP, so it has no admin data: the filter comes without the ids
/// of past downloads (<see cref="ReplayExportFilter.ForManifest"/>).
/// </summary>
public class ReplayArchiveInfo
{
    public string ExportId { get; init; }
    public string Label { get; init; }
    public ReplayExportFilter Filter { get; init; }
}

/// <summary>
/// Writes replays into a ZIP as they arrive, plus manifest.json and manifest.csv at the end. The output can be
/// a non-seekable stream (the HTTP response), so a large export never sits in memory as a whole.
/// </summary>
public static class ReplayArchiveWriter
{
    /// <summary>Replays fetched from replay-service at the same time, per export.</summary>
    public const int ParallelFetches = 4;

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<ReplayArchiveSummary> Write(
        Stream output,
        IReadOnlyList<ReplayExportMatch> matches,
        Func<int, CancellationToken, Task<byte[]>> fetchReplay,
        ReplayArchiveInfo info,
        CancellationToken cancellationToken)
    {
        var entries = new ReplayArchiveEntry[matches.Count];
        var fetched = Channel.CreateBounded<(int Index, byte[] Replay, string Status, string Error)>(ParallelFetches * 2);

        // Stops the fetches when writing fails, so they do not wait forever on a full channel.
        using var stopFetching = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = Task.Run(async () =>
        {
            try
            {
                var options = new ParallelOptions { MaxDegreeOfParallelism = ParallelFetches, CancellationToken = stopFetching.Token };
                await Parallel.ForEachAsync(Enumerable.Range(0, matches.Count), options, async (index, token) =>
                {
                    var result = await Fetch(matches[index].FloMatchId, fetchReplay, token);
                    await fetched.Writer.WriteAsync((index, result.Replay, result.Status, result.Error), token);
                });
                fetched.Writer.Complete();
            }
            catch (Exception ex)
            {
                fetched.Writer.Complete(ex);
            }
        }, CancellationToken.None);

        try
        {
            await WriteZip(output, matches, entries, fetched.Reader, producer, info, cancellationToken);
        }
        finally
        {
            stopFetching.Cancel();
            await producer;
        }

        return new ReplayArchiveSummary
        {
            Written = entries.Count(e => e?.File != null),
            Failed = entries.Count(e => e != null && e.File == null),
            WrittenMatchIds = entries.Where(e => e?.File != null).Select(e => e.Match.Id).ToList(),
        };
    }

    private static async Task WriteZip(
        Stream output,
        IReadOnlyList<ReplayExportMatch> matches,
        ReplayArchiveEntry[] entries,
        ChannelReader<(int Index, byte[] Replay, string Status, string Error)> fetched,
        Task producer,
        ReplayArchiveInfo info,
        CancellationToken cancellationToken)
    {
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            await foreach (var (index, replay, status, error) in fetched.ReadAllAsync(cancellationToken))
            {
                var match = matches[index];
                string file = null;
                if (replay != null)
                {
                    file = $"replays/{FileName(match)}";
                    // Replays are zlib-compressed already; deflating them again gains nothing.
                    var entry = zip.CreateEntry(file, CompressionLevel.NoCompression);
                    entry.LastWriteTime = ZipTime(match.StartTime);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(replay, cancellationToken);
                }
                entries[index] = new ReplayArchiveEntry { File = file, Status = status, Error = error, Match = match };
            }
            await producer;

            await WriteText(zip, "manifest.json", JsonSerializer.Serialize(new
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                info?.ExportId,
                info?.Label,
                info?.Filter,
                Replays = entries,
            }, ManifestJson), cancellationToken);
            await WriteText(zip, "manifest.csv", ManifestCsv(entries), cancellationToken);
        }
    }

    private static async Task<(byte[] Replay, string Status, string Error)> Fetch(
        int floMatchId,
        Func<int, CancellationToken, Task<byte[]>> fetchReplay,
        CancellationToken token)
    {
        try
        {
            var replay = await fetchReplay(floMatchId, token);
            return replay is { Length: > 0 } ? (replay, "ok", null) : (null, "empty", "replay-service returned an empty replay");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, "not-found", ex.Message);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Gone)
        {
            return (null, "archived", ex.Message);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return (null, "timeout", "replay-service did not answer in time");
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            return (null, "failed", ex.Message);
        }
    }

    /// <summary>
    /// For example <c>2026-10-11_1v1_Happy-UD_vs_Lyn-OC_81234567.w3g</c>. Games with more than two players use
    /// the map name instead of the players. The FLO id at the end keeps names unique.
    /// </summary>
    public static string FileName(ReplayExportMatch match)
    {
        var date = match.StartTime.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var mode = match.GameMode.ToString().Replace("GM_", "");
        var players = match.Teams.SelectMany(t => t.Players).ToList();

        var middle = match.Teams.Count == 2 && players.Count == 2
            ? string.Join("_vs_", match.Teams.Select(t => $"{Clean(Name(t.Players[0].BattleTag))}-{PlayedRace(t.Players[0])}"))
            : Clean(match.MapName ?? match.Map ?? "");

        return string.Join("_", new[] { date, mode, middle, match.FloMatchId.ToString(CultureInfo.InvariantCulture) }
            .Where(part => !string.IsNullOrEmpty(part))) + ".w3g";
    }

    private static string PlayedRace(ReplayExportPlayer player) =>
        (player.Race == Race.RnD && player.RndRace is { } played && played != Race.RnD ? played : player.Race).ToString();

    private static string Name(string battleTag) => battleTag?.Split('#')[0] ?? "";

    private static string Clean(string text)
    {
        var kept = new string(text.Where(char.IsLetterOrDigit).ToArray());
        return kept.Length > 24 ? kept[..24] : kept;
    }

    private static string ManifestCsv(IEnumerable<ReplayArchiveEntry> entries)
    {
        var csv = new StringBuilder();
        csv.AppendLine("file,status,match_id,flo_match_id,start_time_utc,end_time_utc,duration_s,patch,game_mode,gateway,season,map,avg_mmr,teams,error");
        foreach (var entry in entries)
        {
            var m = entry.Match;
            var teams = string.Join(" | ", m.Teams.Select(t => string.Join(" + ", t.Players.Select(p =>
                $"{p.BattleTag} ({PlayedRace(p)} {p.OldMmr?.ToString(CultureInfo.InvariantCulture) ?? "?"}{(p.Won ? " W" : "")})"))));

            csv.AppendLine(string.Join(",", new[]
            {
                entry.File, entry.Status, m.Id, m.FloMatchId.ToString(CultureInfo.InvariantCulture),
                m.StartTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                m.EndTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                m.DurationInSeconds.ToString(CultureInfo.InvariantCulture), m.Patch, m.GameMode.ToString(),
                m.GateWay.ToString(), m.Season.ToString(CultureInfo.InvariantCulture), m.MapName ?? m.Map,
                m.AverageMmr?.ToString(CultureInfo.InvariantCulture), teams, entry.Error,
            }.Select(CsvField)));
        }
        return csv.ToString();
    }

    private static string CsvField(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static async Task WriteText(ZipArchive zip, string name, string text, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
    }

    // ZIP timestamps cover 1980 to 2107 only.
    private static DateTimeOffset ZipTime(DateTimeOffset time) =>
        time.Year is >= 1980 and < 2108 ? time : new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
