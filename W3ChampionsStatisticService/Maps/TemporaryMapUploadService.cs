using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Maps;
using W3C.Domain.MatchmakingService;
using W3C.Domain.MatchmakingService.Contracts;
using W3C.Domain.UpdateService;
using W3C.Domain.UpdateService.Contracts;
using W3ChampionsStatisticService.Sessions;
using static W3ChampionsStatisticService.Maps.TemporaryMapUploadRules;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// The §6.3 upload orchestration. Three paths come out of the sha1 dedupe probe: a present record (nothing stored, no
/// quota), a deleted record (verify the proof WITHOUT mutating, store the bytes at the record's own path, then flip
/// fileState to present), and no record (spend quota, store the bytes, create the record).
/// <para>
/// Before the bytes are stored every call takes the request's token, and a failure DURING the update-service upload is
/// never compensated: whether the bytes landed is unknown, and the fileKey may back another record. Once they are
/// stored everything is uncancellable. A failed digest or layout check compensates with an update-service delete
/// directly; any failure of a matchmaking write first re-probes by sha1 once (F-A), because matchmaking can commit and
/// still answer an error.
/// </para>
/// <para>
/// Uploads of the same fileKey serialise on <see cref="TemporaryMapFileKeyLock"/> from just before the store until any
/// compensation is done, so one upload never deletes bytes another has just stored (R-I1).
/// </para>
/// <para>NEVER log mapProof, proofHash or MapProofHash (design spec §10.3). sha1, map ids and fileKeys are fine.</para>
/// </summary>
public class TemporaryMapUploadService(
    MatchmakingServiceClient matchmakingServiceClient,
    UpdateServiceClient updateServiceClient,
    MintRateLimiter rateLimiter,
    ILogger<TemporaryMapUploadService> logger)
{
    /// <summary>One lock per process, whatever lifetime the service is registered with (single-instance, like MintRateLimiter).</summary>
    private static readonly TemporaryMapFileKeyLock ProcessFileKeyLock = new();

    private readonly MatchmakingServiceClient _matchmakingServiceClient = matchmakingServiceClient;
    private readonly UpdateServiceClient _updateServiceClient = updateServiceClient;
    private readonly MintRateLimiter _rateLimiter = rateLimiter;
    private readonly ILogger<TemporaryMapUploadService> _logger = logger;
    private TemporaryMapCompensation _compensation;

    /// <summary>Test seam: the spool directory; null means <see cref="TemporaryMapLimits.TempUploadDir"/>.</summary>
    internal string SpoolDirectory { get; init; }

    /// <summary>Where this service spools: the seam when set, otherwise the shared upload directory Task 6 purges.</summary>
    internal string EffectiveSpoolDirectory => SpoolDirectory ?? TemporaryMapLimits.TempUploadDir;

    /// <summary>Test seam: how compensation waits before each retry. Never cancellable.</summary>
    internal Func<TimeSpan, Task> WaitAsync { get; init; } = delay => Task.Delay(delay, CancellationToken.None);

    /// <summary>Built on first use, after the init-only seams are set.</summary>
    private TemporaryMapCompensation Compensation
        => _compensation ??= new TemporaryMapCompensation(_updateServiceClient, _logger) { WaitAsync = WaitAsync };

    /// <summary>Test seam: the per-fileKey lock; production shares <see cref="ProcessFileKeyLock"/>.</summary>
    internal TemporaryMapFileKeyLock FileKeyLock { get; init; } = ProcessFileKeyLock;

    /// <summary>
    /// Spools the body, then orchestrates. Escapes: <see cref="TemporaryMapUploadException"/> (its status and body), the
    /// reader's <see cref="TemporaryMapSpoolException"/>, an <see cref="IOException"/> from reading the body, and
    /// <see cref="OperationCanceledException"/> once <paramref name="cancellationToken"/> is cancelled. Every outcome but
    /// a client abort is counted once in <see cref="TemporaryMapMetrics.Uploads"/>. A null or blank
    /// <paramref name="battleTag"/> is a caller bug (the controller fails closed before this): <see cref="ArgumentException"/>,
    /// before the body is touched and without a count.
    /// </summary>
    public async Task<TemporaryMapUploadOutcome> HandleUploadAsync(
        Stream body, string contentType, string battleTag, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(battleTag);
        try
        {
            using var upload = await TemporaryMapUploadReader.ReadAsync(
                body, contentType, EffectiveSpoolDirectory, TemporaryMapLimits.MaxFileBytes, cancellationToken);
            var (outcome, result) = await OrchestrateAsync(upload, battleTag, cancellationToken);
            TemporaryMapMetrics.Uploads.WithLabels(result).Inc();
            return outcome;
        }
        catch (Exception ex) when (!(cancellationToken.IsCancellationRequested && ex is OperationCanceledException or IOException))
        {
            TemporaryMapMetrics.Uploads.WithLabels(ex switch
            {
                TemporaryMapUploadException { StatusCode: < 500 } or IOException => TemporaryMapMetrics.Results.Rejected,
                TemporaryMapSpoolException => TemporaryMapMetrics.Results.ServerError,
                _ => TemporaryMapMetrics.Results.UpstreamError,
            }).Inc();
            throw;
        }
    }

    private async Task<(TemporaryMapUploadOutcome, string)> OrchestrateAsync(
        TemporaryMapUpload upload, string battleTag, CancellationToken cancellationToken)
    {
        // 1. The client's sha1 is a hint only; the authoritative one came off the wire.
        if (!string.Equals(upload.Metadata.Sha1?.ToLowerInvariant(), upload.Sha1, StringComparison.Ordinal))
        {
            throw new TemporaryMapUploadException(StatusCodes.Status400BadRequest, "SHA1_MISMATCH");
        }

        _logger.LogInformation("Temporary map upload from {BattleTag}: sha1 {Sha1}, {SizeBytes} bytes, launcher {LauncherVersion}",
            battleTag, upload.Sha1, upload.SizeBytes, LauncherVersionForLog(upload.Metadata.Capture?.LauncherVersion));

        // 2. Dedupe by sha1.
        var existing = await BeforeStoreAsync("sha1 dedupe probe",
            ct => _matchmakingServiceClient.GetTemporaryMapBySha1(upload.Sha1, ct), cancellationToken);
        var proofHash = MapProof.Hash(upload.MapProof);
        return existing?.FileState switch
        {
            _ when existing == null => await CreateAsync(upload, proofHash, battleTag, cancellationToken),
            TemporaryMapFileStates.Present => Deduped(existing, upload.Sha1),
            TemporaryMapFileStates.Deleted => await RestoreAsync(existing, upload, proofHash, battleTag, cancellationToken),
            _ => throw UnknownFileState(existing.Id, existing.FileState),
        };
    }

    private async Task<(TemporaryMapUploadOutcome, string)> CreateAsync(
        TemporaryMapUpload upload, string proofHash, string battleTag, CancellationToken cancellationToken)
    {
        // 3. Quota: only a genuinely new record costs one. MintRateLimiter has no release, so the token is kept even
        // when the update-service 409 re-probe below turns this upload into a dedupe — a race-only overcount (S5).
        if (!_rateLimiter.TryAcquire($"tm-upload:{battleTag}", TemporaryMapLimits.UploadsPerHourPerBattleTag, DateTime.UtcNow,
                TemporaryMapLimits.UploadQuotaWindow, out var retryAfter))
        {
            throw new TemporaryMapUploadException(StatusCodes.Status429TooManyRequests, "QUOTA_EXCEEDED",
                new { code = "QUOTA_EXCEEDED", retryAfterSeconds = RetryAfterSeconds(retryAfter) });
        }

        // 4. Server-generated fileKey: the uploader's name only ever contributes a sanitised base.
        var fileKey = TemporaryMapNaming.BuildFileKey(upload.Metadata.OriginalFileName, upload.Sha1, upload.Extension);

        // R-I1: held until this upload's store, record write and compensation are all done. Waiting comes before anything
        // is stored, so an abort while waiting has nothing to undo.
        using var fileKeyHeld = await FileKeyLock.AcquireAsync(fileKey, cancellationToken);
        var stored = await StoreBytesAsync(upload, fileKey, mapId: 0, battleTag, cancellationToken);
        if (stored == null)
        {
            // update-service already holds this fileKey. If matchmaking knows our sha1 by now, that record wins.
            var known = await BeforeStoreAsync("sha1 re-probe after an update-service conflict",
                ct => _matchmakingServiceClient.GetTemporaryMapBySha1(upload.Sha1, ct), cancellationToken);
            if (known != null)
            {
                return known.FileState == TemporaryMapFileStates.Present
                    ? Deduped(known, upload.Sha1)
                    : throw UnknownFileState(known.Id, known.FileState);
            }

            var claimant = await ProbePathAsync(fileKey, cancellationToken);
            stored = await ReplaceStrayFileAsync(upload, fileKey, mapId: 0, claimant, battleTag, cancellationToken);
        }

        return await AfterStoreAsync(fileKey, async () =>
        {
            await VerifyDigestsAsync(stored, upload, proofHash, fileKey);

            // 6. The capture has to describe a lobby this map can actually have (new maps only).
            if (!IsPossibleLayout(upload.Metadata.Capture, stored.MetaData))
            {
                _logger.LogInformation("Upload sha1 {Sha1} captured a lobby this map cannot have; compensating {FileKey}", upload.Sha1, fileKey);
                await Compensation.DeleteAsync(fileKey);
                throw new TemporaryMapUploadException(StatusCodes.Status400BadRequest, "INVALID_LAYOUT");
            }

            return await CreateRecordAsync(upload, stored.MetaData, fileKey, battleTag);
        });
    }

    private async Task<(TemporaryMapUploadOutcome, string)> CreateRecordAsync(
        TemporaryMapUpload upload, GameMap parsed, string fileKey, string battleTag)
    {
        // 7. Create the record.
        var capture = upload.Metadata.Capture;
        CreateTemporaryMapResult created;
        try
        {
            created = await _matchmakingServiceClient.CreateTemporaryMap(new CreateTemporaryMapRequest
            {
                Sha1 = upload.Sha1,
                Uploader = battleTag,
                OriginalFileName = TemporaryMapNaming.RemoveControlCharacters(upload.Metadata.OriginalFileName),
                MapProof = upload.MapProof,
                MaxTeams = capture.MaxTeams,
                SlotCount = capture.SlotCount,
                LobbyMode = capture.LobbyMode,
                MappedForces = ForwardedForces(capture.MappedForces),
                GameMap = ToGameMap(parsed, fileKey),
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // F-A: an error answer does not prove nothing was written (matchmaking answers 400 when its refresh fails after
            // the insert; a proxy can answer 5xx after processing), so ask before deleting bytes a record may point at.
            LogProofCarryingCallFailure(ex, "create", upload.Sha1, fileKey);
            var record = await ProbeAfterStoreAsync(upload.Sha1, fileKey, restoringMapId: null)
                         ?? throw await CompensateAsync(fileKey, "No temporary map has sha1 {Sha1} after the failed create", upload.Sha1);
            return await KeepTheWinnerAsync(record, upload.Sha1, fileKey);
        }

        if (!created.Created)
        {
            return await KeepTheWinnerAsync(created.Map, upload.Sha1, fileKey);
        }

        _logger.LogInformation("Created temporary map {MapId} at {FileKey} for {BattleTag}", created.Map.Id, fileKey, battleTag);
        return Outcome(TemporaryMapMetrics.Results.Created, created.Map.Id, fileKey, created.Map.Name, upload.Sha1);
    }

    /// <summary>
    /// A record of this sha1 exists after our create. Our file is stray only if that record is a present temporary file at a
    /// different path; any other record leaves the outcome unknown (S-L2), so nothing is deleted.
    /// </summary>
    private async Task<(TemporaryMapUploadOutcome, string)> KeepTheWinnerAsync(MapContract winner, string sha1, string fileKey)
    {
        if (winner.FileState != TemporaryMapFileStates.Present || !TemporaryMapKeys.IsFilePath(winner.Path))
        {
            throw Upstream("Temporary map {MapId} holds sha1 {Sha1} with fileState {FileState} at {Path}; leaving {FileKey}, which it may use",
                winner.Id, sha1, winner.FileState, winner.Path, fileKey);
        }

        if (!string.Equals(winner.Path, fileKey, StringComparison.Ordinal))
        {
            await Compensation.DeleteAsync(fileKey);
        }

        return Deduped(winner, sha1);
    }

    private async Task<(TemporaryMapUploadOutcome, string)> RestoreAsync(
        MapContract existing, TemporaryMapUpload upload, string proofHash, string battleTag, CancellationToken cancellationToken)
    {
        // 4a. VERIFY BEFORE MUTATE: this route writes nothing, so a wrong proof costs no state.
        var verified = await BeforeStoreAsync("proof verification",
                           ct => _matchmakingServiceClient.VerifyTemporaryMapProof(proofHash, ct), cancellationToken, carriesProof: true)
                       ?? throw new TemporaryMapUploadException(StatusCodes.Status400BadRequest, "PROOF_MISMATCH");
        if (verified.MapId != existing.Id)
        {
            // sha1 is the dedupe key and proofHash the credential key; both derive from the same bytes (§5.1).
            _logger.LogError("TEMP_MAP_KEY_MISMATCH: sha1 {Sha1} resolves to map {Sha1MapId} but its proof to map {ProofMapId}",
                upload.Sha1, existing.Id, verified.MapId);
            throw KeyMismatch();
        }

        if (verified.FileState == TemporaryMapFileStates.Present)
        {
            // A concurrent restore already put the bytes back: a dedupe hit, nothing written (S4).
            return Deduped(existing, upload.Sha1);
        }

        if (verified.FileState != TemporaryMapFileStates.Deleted)
        {
            throw UnknownFileState(verified.MapId, verified.FileState);
        }

        // 4b. The record's stored path is authoritative; the uploader's file name plays no part. Before anything is
        // written at it, it has to be exactly a fileKey this service could have built (S-I2).
        var fileKey = verified.Path;
        if (!TemporaryMapNaming.IsFileKey(fileKey))
        {
            _logger.LogError("TEMP_MAP_KEY_MISMATCH: temporary map {MapId} stores {FileKey}, which is not a temporary map file", existing.Id, fileKey);
            throw KeyMismatch();
        }

        // R-I1: another restore of this record waits here until this one is done, compensation included.
        using var fileKeyHeld = await FileKeyLock.AcquireAsync(fileKey, cancellationToken);
        var stored = await StoreBytesAsync(upload, fileKey, existing.Id, battleTag, cancellationToken);
        if (stored == null)
        {
            var claimant = await ProbePathAsync(fileKey, cancellationToken);
            if (claimant?.Id == existing.Id && claimant.FileState == TemporaryMapFileStates.Present)
            {
                // S-M1, as S4: a concurrent restore of this record already stored the bytes and flipped it.
                return Deduped(claimant, upload.Sha1);
            }

            stored = await ReplaceStrayFileAsync(upload, fileKey, existing.Id, claimant, battleTag, cancellationToken);
        }

        return await AfterStoreAsync(fileKey, async () =>
        {
            // The capture is not validated on a restore: the record's layout is authoritative (B4).
            await VerifyDigestsAsync(stored, upload, proofHash, fileKey);
            await MarkRestoredAsync(existing.Id, upload, fileKey, battleTag);
            _logger.LogInformation("Restored temporary map {MapId} at {FileKey} for {BattleTag}", existing.Id, fileKey, battleTag);
            return Outcome(TemporaryMapMetrics.Results.Restored, existing.Id, fileKey, existing.Name, upload.Sha1);
        });
    }

    private async Task MarkRestoredAsync(int mapId, TemporaryMapUpload upload, string fileKey, string battleTag)
    {
        // 4c. Only now, with verified bytes stored, does the record change.
        try
        {
            await _matchmakingServiceClient.MarkTemporaryMapFileRestored(mapId,
                new TemporaryMapFileRestoredRequest { Sha1 = upload.Sha1, Uploader = battleTag, MapProof = upload.MapProof },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            // F-A, as on create: the flip may have committed although the call failed.
            LogProofCarryingCallFailure(ex, "file-restored", upload.Sha1, fileKey);
            var record = await ProbeAfterStoreAsync(upload.Sha1, fileKey, restoringMapId: mapId);
            if (record?.FileState == TemporaryMapFileStates.Present)
            {
                return;
            }

            if (record != null && record.FileState != TemporaryMapFileStates.Deleted)
            {
                throw UnknownFileState(record.Id, record.FileState);
            }

            throw await CompensateAsync(fileKey, "Temporary map sha1 {Sha1} is still not present after the failed file-restored", upload.Sha1);
        }
    }

    /// <summary>The by-path probe after an update-service 409: which record, if any, claims <paramref name="fileKey"/>.</summary>
    private Task<MapContract> ProbePathAsync(string fileKey, CancellationToken cancellationToken)
        => BeforeStoreAsync("path probe after an update-service conflict",
            ct => _matchmakingServiceClient.GetTemporaryMapByPath(fileKey, ct), cancellationToken);

    /// <summary>
    /// update-service answered 409 for <paramref name="fileKey"/>, and <paramref name="claimant"/> is the record that claims
    /// it (null: none). Deleting that file is only safe when no OTHER record claims the path (H1: the 8-hex fileKey suffix
    /// can be ground to collide with a live map), or when the record being restored claims it and is still deleted. Returns
    /// the one retried upload.
    /// <para>
    /// Sound only under two assumptions (S-I1): matchmaking's unique index on <c>gameMap.path</c>, so at most one record
    /// claims a path; and byte-exact, case-sensitive file identity at update-service, so the by-path answer is about the
    /// very file the 409 reported.
    /// </para>
    /// </summary>
    private async Task<MapFileData> ReplaceStrayFileAsync(
        TemporaryMapUpload upload, string fileKey, int mapId, MapContract claimant, string battleTag, CancellationToken cancellationToken)
    {
        if (claimant != null && (mapId == 0 || claimant.Id != mapId))
        {
            throw Upstream("Refusing to replace {FileKey}: temporary map {ClaimantMapId} (sha1 {ClaimantSha1}) claims it, " +
                           "not this upload of sha1 {Sha1} for map {MapId} (0 is a new map)",
                fileKey, claimant.Id, claimant.GameMap?.Sha1, upload.Sha1, mapId);
        }

        if (claimant != null && claimant.FileState != TemporaryMapFileStates.Deleted)
        {
            throw UnknownFileState(claimant.Id, claimant.FileState);
        }

        await BeforeStoreAsync("stray file delete", async ct =>
        {
            await _updateServiceClient.DeleteMapFileByPathAsync(fileKey, ct);
            return true;
        }, cancellationToken);

        // F-B: the stray file is gone, so only this retry can put bytes back at the fileKey; a client abort must not stop it.
        return await StoreBytesAsync(upload, fileKey, mapId, battleTag, CancellationToken.None)
               ?? throw Upstream("update-service still reports a conflict at {FileKey} after deleting the stray file", fileKey);
    }

    /// <summary>Returns null when update-service reported a duplicate target path (409).</summary>
    private async Task<MapFileData> StoreBytesAsync(
        TemporaryMapUpload upload, string fileKey, int mapId, string battleTag, CancellationToken cancellationToken)
    {
        try
        {
            // update-service disposes the stream with its request, so every attempt opens its own.
            await using var bytes = upload.OpenRead();
            return await _updateServiceClient.UploadTemporaryMapAsync(
                bytes, TemporaryMapNaming.UpdateServiceFileName(fileKey), mapId, battleTag, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }
        catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "update-service upload of {FileKey} failed; not compensated, whether the bytes landed is unknown", fileKey);
            throw Upstream();
        }
    }

    /// <summary>
    /// A call before the bytes are stored: a client abort propagates, any other failure is a 502 with nothing to undo.
    /// When the call <paramref name="carriesProof"/>, only the exception's type and status are logged (S-L3).
    /// </summary>
    private async Task<T> BeforeStoreAsync<T>(
        string step, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken, bool carriesProof = false)
    {
        try
        {
            return await call(cancellationToken);
        }
        catch (Exception ex) when (!IsCancellation(ex, cancellationToken))
        {
            if (carriesProof)
            {
                _logger.LogWarning("Temporary map upload failed at the {Step} with {ExceptionType} (status {StatusCode}), before any bytes were stored",
                    step, ex.GetType().Name, StatusOf(ex));
            }
            else
            {
                _logger.LogWarning(ex, "Temporary map upload failed at the {Step}, before any bytes were stored", step);
            }

            throw Upstream();
        }
    }

    /// <summary>
    /// S-L3: matchmaking echoes raw error text, which can quote the proof the request carried, so a failed create or
    /// file-restored is logged by exception type and status only — never the exception or its message.
    /// </summary>
    private void LogProofCarryingCallFailure(Exception ex, string call, string sha1, string fileKey)
        => _logger.LogWarning("matchmaking {Call} for sha1 {Sha1} failed with {ExceptionType} (status {StatusCode}); " +
                              "re-probing by sha1 before deciding about {FileKey}", call, sha1, ex.GetType().Name, StatusOf(ex), fileKey);

    /// <summary>After the bytes are stored, an exception no step decided on compensates (I1(b)).</summary>
    private async Task<(TemporaryMapUploadOutcome, string)> AfterStoreAsync(string fileKey, Func<Task<(TemporaryMapUploadOutcome, string)>> steps)
    {
        try
        {
            return await steps();
        }
        catch (Exception ex) when (ex is not TemporaryMapUploadException)
        {
            _logger.LogError(ex, "Unexpected failure after storing temporary map file {FileKey}; compensating", fileKey);
            await Compensation.DeleteAsync(fileKey);
            throw Upstream();
        }
    }

    /// <summary>
    /// The single sha1 re-probe after a failed matchmaking write (F-A). If it fails too, nothing is compensated, and the
    /// warning names who reclaims the bytes: the sweep for a new map, a later restore for <paramref name="restoringMapId"/>.
    /// </summary>
    private async Task<MapContract> ProbeAfterStoreAsync(string sha1, string fileKey, int? restoringMapId)
    {
        try
        {
            return await _matchmakingServiceClient.GetTemporaryMapBySha1(sha1, CancellationToken.None);
        }
        catch (Exception ex) when (restoringMapId == null)
        {
            _logger.LogWarning(ex, "Could not re-probe sha1 {Sha1}; leaving {FileKey} for the reconciliation sweep", sha1, fileKey);
            throw Upstream();
        }
        catch (Exception ex)
        {
            // The record claims this path, so the sweep never reclaims it (R-Minor2).
            _logger.LogWarning(ex, "Could not re-probe sha1 {Sha1} after file-restored for temporary map {MapId}; the bytes at {FileKey} " +
                                   "stay under that record, and while it is deleted a later restore replaces them", sha1, restoringMapId, fileKey);
            throw Upstream();
        }
    }

    private async Task VerifyDigestsAsync(MapFileData stored, TemporaryMapUpload upload, string proofHash, string fileKey)
    {
        // 5. Both services derived these from the same bytes; a mismatch means one of them read different bytes.
        var parsedSha1 = stored.MetaData?.Sha1?.ToLowerInvariant();
        if (string.Equals(parsedSha1, upload.Sha1, StringComparison.Ordinal)
            && string.Equals(stored.MapProofHash, proofHash, StringComparison.Ordinal))
        {
            return;
        }

        _logger.LogWarning("update-service derived other digests for sha1 {Sha1} (parsed sha1 {ParsedSha1}); compensating {FileKey}",
            upload.Sha1, parsedSha1, fileKey);
        await Compensation.DeleteAsync(fileKey);
        throw new TemporaryMapUploadException(StatusCodes.Status502BadGateway, "PARSER_MISMATCH");
    }

    /// <summary>Logs why the stored bytes are rolled back, rolls them back, and returns the 502 to throw.</summary>
    private async Task<TemporaryMapUploadException> CompensateAsync(string fileKey, string why, params object[] args)
    {
        _logger.LogWarning(why + "; compensating {FileKey}", [.. args, fileKey]);
        await Compensation.DeleteAsync(fileKey);
        return Upstream();
    }

    /// <summary>Whole seconds, rounded up and clamped to [1, int.MaxValue] before the cast so a saturated window cannot wrap.</summary>
    internal static int RetryAfterSeconds(TimeSpan retryAfter)
        => (int)Math.Clamp(Math.Ceiling(retryAfter.TotalSeconds), 1, int.MaxValue);

    private static bool IsCancellation(Exception ex, CancellationToken cancellationToken)
        => ex is OperationCanceledException && cancellationToken.IsCancellationRequested;

    private static int? StatusOf(Exception ex) => (int?)(ex as HttpRequestException)?.StatusCode;

    private TemporaryMapUploadException UnknownFileState(int mapId, string fileState)
        => Upstream("matchmaking answered temporary map {MapId} with fileState {FileState}, which this step cannot act on", mapId, fileState);

    /// <summary>Logs why at warning and returns the 502 to throw.</summary>
    private TemporaryMapUploadException Upstream(string why, params object[] args)
    {
        _logger.LogWarning(why, args);
        return Upstream();
    }

    private static TemporaryMapUploadException Upstream() => new(StatusCodes.Status502BadGateway, "UPSTREAM");

    private static TemporaryMapUploadException KeyMismatch() => new(StatusCodes.Status500InternalServerError, "TEMP_MAP_KEY_MISMATCH");

    /// <summary>A 200 never hands out a path that is not a temporary map file (S-L2).</summary>
    private (TemporaryMapUploadOutcome, string) Deduped(MapContract map, string sha1)
        => TemporaryMapKeys.IsFilePath(map.Path)
            ? Outcome(TemporaryMapMetrics.Results.Deduped, map.Id, map.Path, map.Name, sha1)
            : throw Upstream("Temporary map {MapId} with sha1 {Sha1} has no temporary map file path ({Path})", map.Id, sha1, map.Path);

    private static (TemporaryMapUploadOutcome, string) Outcome(string result, int mapId, string path, string name, string sha1) => (new TemporaryMapUploadOutcome
    {
        Created = result == TemporaryMapMetrics.Results.Created,
        Response = new TemporaryMapUploadResponse { MapId = mapId, Path = path, Name = name, Sha1 = sha1 },
    }, result);
}
