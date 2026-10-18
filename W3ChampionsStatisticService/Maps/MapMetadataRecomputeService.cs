using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Tracing;
using W3C.Domain.UpdateService;
using W3C.Domain.UpdateService.Contracts;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// Recomputes the checksum/crc32 of every matchmaking map's file on update-service, then (unless it is a
/// dry run) copies the corrected values into matchmaking by sha1. Every map gets a report row, failures
/// included; nothing is dropped silently.
/// </summary>
[Trace]
public class MapMetadataRecomputeService(MatchmakingServiceClient matchmakingServiceClient, UpdateServiceClient updateServiceClient)
{
    private readonly MatchmakingServiceClient _matchmakingServiceClient = matchmakingServiceClient;
    private readonly UpdateServiceClient _updateServiceClient = updateServiceClient;

    /// <summary>Update-service recomputes a request synchronously, so paths go in sequential chunks.</summary>
    public const int UpdateServicePathsPerRequest = 50;
    /// <summary>Matchmaking accepts 1..2000 updates per request.</summary>
    public const int MatchmakingUpdatesPerRequest = 2000;

    /// <param name="mapIds">Only these matchmaking maps; null means every map.</param>
    public async Task<MapMetadataRecomputeReport> Recompute(bool dryRun, IReadOnlyCollection<int> mapIds = null)
    {
        var maps = await CallUpstream(MapMetadataRecomputeUpstreamException.Matchmaking, _matchmakingServiceClient.GetAllMapsOrThrow);
        var selected = mapIds == null ? maps.Items : maps.Items.Where(m => mapIds.Contains(m.Id)).ToArray();
        var unknownMapIds = mapIds == null ? [] : mapIds.Distinct().Where(id => maps.Items.All(m => m.Id != id)).ToList();
        var rows = selected.Select(ToRow).ToList();

        // Same comparer as the join, so paths differing only in case are sent (and joined) once.
        var paths = rows.Select(r => r.UpdateServicePath).Where(p => p != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var results = await RecomputeInChunks(paths, dryRun);

        var unmatchedResults = JoinResults(rows, results);
        // The matchmaking patch is keyed by sha1, so a sha1 is blocked when any result for it failed, and when any
        // matchmaking map carrying it is not cleanly recomputed (e.g. its own result has no or another sha1).
        var failedSha1s = results.Where(HasFailed).Select(r => r.Sha1)
            .Concat(rows.Where(r => r.Problem != null).Select(r => r.Sha1))
            .Where(s => s != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var patches = PlanMatchmakingPatches(rows, failedSha1s);

        string mmPatchError = null;
        List<GameMapMetadataUpdateResult> mmResults = [];
        var mmSent = 0;
        if (!dryRun && patches.Count > 0)
        {
            (mmResults, mmSent, mmPatchError) = await ApplyMatchmakingPatches(rows, patches);
        }

        var report = new MapMetadataRecomputeReport
        {
            DryRun = dryRun,
            Maps = rows,
            UnmatchedResults = unmatchedResults,
            UnknownMapIds = unknownMapIds,
            MmPatchError = mmPatchError,
            Summary = new MapMetadataRecomputeSummary
            {
                MmMaps = rows.Count,
                PathsSent = paths.Count,
                Unchanged = rows.Count(r => r.Status == MapMetadataRecomputeStatus.Unchanged),
                Changed = rows.Count(r => r.Status == MapMetadataRecomputeStatus.Changed),
                Problems = rows.Count(r => r.Problem != null),
                UnmatchedResults = unmatchedResults.Count,
                UnknownMapIds = unknownMapIds.Count,
                MmPatchNeeded = patches.Count,
                MmPatchSent = mmSent,
                MmMatched = mmResults.Sum(r => r.Matched),
                MmModified = mmResults.Sum(r => r.Modified),
            },
        };

        Log.Information(
            "Map metadata recompute (dryRun={DryRun}): {MmMaps} maps, {Changed} changed, {Problems} problems, {Unmatched} unmatched results, {PatchSent} sha1s patched in matchmaking, patch error: {PatchError}",
            dryRun, report.Summary.MmMaps, report.Summary.Changed, report.Summary.Problems, report.Summary.UnmatchedResults, report.Summary.MmPatchSent, mmPatchError);

        return report;
    }

    /// <summary>
    /// Any failed chunk aborts the whole run before matchmaking is touched: matchmaking is only patched from
    /// complete data. An empty path list is never sent (update-service reads a missing list as "every file").
    /// </summary>
    private async Task<List<MapMetadataRecomputeResult>> RecomputeInChunks(List<string> paths, bool dryRun)
    {
        var results = new List<MapMetadataRecomputeResult>();
        var completed = 0;
        foreach (var chunk in paths.Chunk(UpdateServicePathsPerRequest))
        {
            try
            {
                var response = await CallUpstream(MapMetadataRecomputeUpstreamException.UpdateService,
                    () => _updateServiceClient.RecomputeMapMetadata(new MapMetadataRecomputeRequest { DryRun = dryRun, Paths = [.. chunk] }));
                // A missing dryRun (null) is a mismatch too: only an explicit echo of the requested mode is trusted.
                if (response.DryRun != dryRun)
                {
                    throw new MapMetadataRecomputeUpstreamException(MapMetadataRecomputeUpstreamException.UpdateService, null,
                        $"update-service answered dryRun={response.DryRun?.ToString() ?? "(missing)"} for a dryRun={dryRun} request", null)
                    {
                        Reason = MapMetadataRecomputeUpstreamException.DryRunMismatch,
                    };
                }

                results.AddRange(response.Results);
                completed += chunk.Length;
            }
            catch (MapMetadataRecomputeUpstreamException ex)
            {
                var written = dryRun || completed == 0 ? "" : $" Update-service already stored the results of the first {completed} paths; rerun to finish.";
                throw new MapMetadataRecomputeUpstreamException(ex.Service, ex.UpstreamStatus,
                    $"{ex.Message} (after {completed} of {paths.Count} paths). Matchmaking was not patched.{written}", ex.InnerException)
                {
                    PathsCompleted = completed,
                    PathsTotal = paths.Count,
                    Reason = ex.Reason,
                };
            }
        }

        return results;
    }

    private static MapMetadataRecomputeMapReport ToRow(MapContract map)
    {
        var row = new MapMetadataRecomputeMapReport
        {
            MmMapId = map.Id,
            Name = map.Name,
            Path = map.GameMap?.Path,
            Sha1 = map.GameMap?.Sha1,
            MmChecksum = map.GameMap?.Checksum,
            MmCrc32 = map.GameMap?.Crc32,
            UpdateServicePath = MapFilePaths.ToUpdateServiceFilePath(map.GameMap?.Path),
        };

        if (row.UpdateServicePath == null)
        {
            row.Status = row.Problem = MapMetadataRecomputeProblems.NoPath;
        }

        return row;
    }

    /// <summary>
    /// Joins each row to the result for its own path; only when there is none, to a result with the same sha1
    /// from another path. Returns the results no row took.
    /// </summary>
    private static List<MapMetadataRecomputeResult> JoinResults(List<MapMetadataRecomputeMapReport> rows, List<MapMetadataRecomputeResult> results)
    {
        var used = new HashSet<MapMetadataRecomputeResult>(ReferenceEqualityComparer.Instance);

        foreach (var row in rows.Where(r => r.UpdateServicePath != null))
        {
            var result = results.FirstOrDefault(r => SamePath(r.Path, row.UpdateServicePath))
                ?? results.FirstOrDefault(r => SameSha1(r.Sha1, row.Sha1));

            if (result == null)
            {
                row.Status = row.Problem = MapMetadataRecomputeProblems.NoResult;
                continue;
            }

            used.Add(result);
            row.UpdateServiceSha1 = result.Sha1;
            row.OldChecksum = result.OldChecksum;
            row.NewChecksum = result.NewChecksum;
            row.OldCrc32 = result.OldCrc32;
            row.NewCrc32 = result.NewCrc32;
            row.Status = result.Status;
            row.Error = result.Error;

            if (!IsRecomputed(result.Status))
            {
                row.Problem = result.Status ?? MapMetadataRecomputeProblems.NoResult;
            }
            else if (HasFailed(result))
            {
                // Defensive: a changed/unchanged result that still carries an error is treated as not stored.
                row.Problem = MapMetadataRecomputeProblems.UpdateServiceWriteFailed;
            }
            else if (!SameSha1(result.Sha1, row.Sha1))
            {
                row.Problem = MapMetadataRecomputeProblems.Sha1Differs;
            }
        }

        return results.Where(r => !used.Contains(r)).ToList();
    }

    /// <summary>
    /// A map needs patching when update-service changed its values, or when matchmaking still holds values
    /// that differ from the recomputed ones (e.g. a rerun after update-service was fixed but matchmaking was
    /// not). One update per sha1.
    /// </summary>
    private static List<GameMapMetadataUpdate> PlanMatchmakingPatches(List<MapMetadataRecomputeMapReport> rows, HashSet<string> failedSha1s)
    {
        var candidates = new List<MapMetadataRecomputeMapReport>();
        foreach (var row in rows.Where(r => r.Problem == null))
        {
            if (!IsLowercaseSha1(row.Sha1))
            {
                row.Problem = MapMetadataRecomputeProblems.MmSha1NotLowercaseHex;
                continue;
            }

            if (failedSha1s.Contains(row.Sha1))
            {
                row.Problem = MapMetadataRecomputeProblems.Sha1HasFailedResult;
                continue;
            }

            if (!IsUint32(row.NewChecksum) || !IsUint32(row.NewCrc32))
            {
                row.Problem = MapMetadataRecomputeProblems.InvalidNewValues;
                continue;
            }

            if (row.Status == MapMetadataRecomputeStatus.Changed || row.MmChecksum != row.NewChecksum || row.MmCrc32 != row.NewCrc32)
            {
                candidates.Add(row);
            }
        }

        var patches = new List<GameMapMetadataUpdate>();
        foreach (var group in candidates.GroupBy(r => r.Sha1.ToLowerInvariant()))
        {
            var values = group.Select(r => (Checksum: r.NewChecksum!.Value, Crc32: r.NewCrc32!.Value)).Distinct().ToList();
            if (values.Count > 1)
            {
                foreach (var row in group) row.Problem = MapMetadataRecomputeProblems.ConflictingValues;
                continue;
            }

            foreach (var row in group) row.MmPatchNeeded = true;
            patches.Add(new GameMapMetadataUpdate { Sha1 = group.Key, Checksum = values[0].Checksum, Crc32 = values[0].Crc32 });
        }

        return patches;
    }

    /// <summary>Sends the patches in chunks; a failed chunk marks only its own rows and the others still go out.</summary>
    private async Task<(List<GameMapMetadataUpdateResult> Results, int Sent, string Error)> ApplyMatchmakingPatches(
        List<MapMetadataRecomputeMapReport> rows,
        List<GameMapMetadataUpdate> patches)
    {
        var rowsBySha1 = rows.Where(r => r.MmPatchNeeded).ToLookup(r => r.Sha1, StringComparer.OrdinalIgnoreCase);
        var allResults = new List<GameMapMetadataUpdateResult>();
        var errors = new List<string>();
        var sent = 0;

        foreach (var chunk in patches.Chunk(MatchmakingUpdatesPerRequest))
        {
            sent += chunk.Length;
            var chunkRows = chunk.SelectMany(p => rowsBySha1[p.Sha1]).ToList();
            GameMapMetadataUpdateResponse response;
            try
            {
                response = await _matchmakingServiceClient.UpdateGameMapMetadata(new GameMapMetadataUpdateRequest { Updates = [.. chunk] });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                var status = ex is HttpRequestException { StatusCode: not null } http ? $"HTTP {(int)http.StatusCode}: " : "";
                errors.Add($"Matchmaking game map metadata patch of {chunk.Length} sha1s failed: {status}{ex.Message}");
                Log.Error(ex, "Map metadata recompute: matchmaking patch of {Count} sha1s failed", chunk.Length);
                foreach (var row in chunkRows) row.Problem = MapMetadataRecomputeProblems.MmPatchFailed;
                continue;
            }

            allResults.AddRange(response.Results);
            ApplyMatchmakingResults(chunkRows, response.Results);
        }

        return (allResults, sent, errors.Count == 0 ? null : string.Join("; ", errors));
    }

    private static void ApplyMatchmakingResults(List<MapMetadataRecomputeMapReport> rows, List<GameMapMetadataUpdateResult> results)
    {
        var bySha1 = results
            .Where(r => r.Sha1 != null)
            .GroupBy(r => r.Sha1, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (!bySha1.TryGetValue(row.Sha1, out var result))
            {
                row.Problem = MapMetadataRecomputeProblems.MmNoResult;
                continue;
            }

            row.MmMatched = result.Matched;
            row.MmModified = result.Modified;
            row.MmPatched = result.Matched > 0;
            if (!row.MmPatched) row.Problem = MapMetadataRecomputeProblems.MmNotMatched;
        }
    }

    private static readonly Regex LowercaseSha1 = new("^[0-9a-f]{40}$", RegexOptions.Compiled);

    private static bool IsLowercaseSha1(string sha1) => sha1 != null && LowercaseSha1.IsMatch(sha1);

    private static bool IsUint32(long? value) => value is >= 0 and <= uint.MaxValue;

    /// <summary>A failing status, or (defensively) any result that carries an error, means nothing was stored.</summary>
    private static bool HasFailed(MapMetadataRecomputeResult result) => !IsRecomputed(result.Status) || result.Error != null;

    private static bool IsRecomputed(string status) =>
        status is MapMetadataRecomputeStatus.Changed or MapMetadataRecomputeStatus.Unchanged;

    private static bool SameSha1(string a, string b) =>
        a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string a, string b) =>
        a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static async Task<T> CallUpstream<T>(string service, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (HttpRequestException ex)
        {
            throw new MapMetadataRecomputeUpstreamException(service, (int?)ex.StatusCode, $"{service} call failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new MapMetadataRecomputeUpstreamException(service, null, $"{service} call timed out", ex);
        }
        catch (JsonException ex)
        {
            throw new MapMetadataRecomputeUpstreamException(service, null, $"{service} answered with unreadable JSON", ex);
        }
    }
}
