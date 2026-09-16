# Self-provided custom maps (temporary maps) — website-backend

Implements §6 of the internal design spec (§6, Appendix A). website-backend is the player-facing gateway
in front of the two owners of the actual data.

## What this service does

website-backend owns no map bytes and no map records. It is the only component in the chain that a
*player* may talk to, so it holds the `ADMIN_SECRET` (configuration-supplied — never quote the literal
fallback value anywhere) and fans out to the two owners:

| Concern | Owner | Reached via |
|---|---|---|
| Map record, id, 30-day TTL clock, mapProof storage | matchmaking-service | `MatchmakingServiceClient` |
| `.w3x`/`.w3m` bytes, parsing, keyed download | update-service | `UpdateServiceClient` |

Both temporary-map routes are player routes, not admin routes: any player with a valid JWT may pre-check
and upload, bounded only by the in-memory quotas and the in-flight gate below. The existing admin routes
(`MapsController`) are unchanged except that `GET api/maps` now always sends `x-admin-secret` (see
"`GET api/maps` and `includeTemporary`" below).

## Routes

### `GET api/maps/temporary/status?proofHash=<64 lowercase hex>`

`[BearerRequiresPlayerAuth]`. Answers the *state* only — never an id, path, name or proof — because the
route is keyed by `proofHash`, a hash of a secret only a file holder can compute, so knowing the state of
someone else's map is impossible without already holding the file.

| Status | Body | When |
|---|---|---|
| 200 | `{ state: "ready" }` | matchmaking reports `fileState: present` |
| 200 | `{ state: "expired" }` | matchmaking reports `fileState: deleted` |
| 404 | `{ state: "unknown" }` | no record for that proofHash, **or** `proofHash` is not exactly 64 lowercase hex (checked locally — no matchmaking call) |
| 429 | *(empty)* | the per-battleTag pre-check quota (`PrecheckPerBattleTagPerMinute` = 60/minute) is exhausted |
| 502 | *(empty)* | matchmaking could not be asked (transport failure, timeout, a strict-404 contract violation) **or** it answered a `fileState` this route does not know how to relay |
| 401 | `{ error: "Invalid token" }` | the auth filter rejected a missing/malformed/badly-signed bearer token |
| 401 | *(empty)* | defence-in-depth: the route ran without the filter having published a battleTag (should not happen in production) |
| *(no response)* | — | the client disconnected before the answer was ready |

429 and 502 are deliberately **bare**: no content type, no body, matching Appendix A.4 ("Nothing else is
returned"). `TemporaryMapsController` is deliberately **not** an `[ApiController]` so ASP.NET's client-error
mapping cannot turn those bare statuses into a `application/problem+json` body; it keeps its Swagger/API
Explorer entry through an explicit `[ApiExplorerSettings(IgnoreApi = false)]` instead.

### `POST api/maps/temporary`

`[BearerRequiresPlayerAuth] [DisableFormValueModelBinding] [TemporaryMapUploadBodyLimit]`. Streams a
two-part multipart body (`metadata` JSON part, then `mapFile`) straight off the wire — nothing is ever
buffered in memory.

| Status | Body | When |
|---|---|---|
| 201 | `{ mapId, path, name, sha1 }` | a brand-new record was created |
| 200 | `{ mapId, path, name, sha1 }` | a dedupe hit (same sha1 already `present`) or a successful restore of an expired map |
| 400 | `{ code: "EXTENSION" }` | `originalFileName` is not `.w3x`/`.w3m` |
| 400 | `{ code: "METADATA" }` | malformed multipart (missing/misnamed/misordered part, bad boundary), an oversized (>64 KiB) or unparsable `metadata` JSON part, an `originalFileName` over 255 UTF-16 code units, or a body that ends early / a connection reset that is not a client abort |
| 400 | `{ code: "SHA1_MISMATCH" }` | the client-supplied sha1 hint does not match the server-recomputed sha1 of the bytes |
| 400 | `{ code: "INVALID_LAYOUT" }` | the capture cannot describe a lobby this map can have (new-record path only — a restore never validates the capture, the record's own layout is authoritative) |
| 400 | `{ code: "PROOF_MISMATCH" }` | a restore whose proof matchmaking does not recognise |
| 413 | `{ code: "FILE_TOO_LARGE" }` | over `MaxFileBytes` (256 MiB), whether caught by the reader's own running cap or by Kestrel's own per-action limit (both raised together, see below) |
| 429 | `{ code: "QUOTA_EXCEEDED", retryAfterSeconds }` | any of three independent quotas — see "Concurrency and quotas" |
| 500 | *(empty)* | a local spooling/disk fault (already logged once by the reader) or any other unexpected server fault (a body-stream fault, a cancellation the request did not cause) |
| 500 | `{ code: "TEMP_MAP_KEY_MISMATCH" }` | the sha1 dedupe and the proof verification resolved to two different map ids, or the record's stored path is not a well-formed §6.4 temporary-map file path |
| 502 | `{ code: "UPSTREAM" }` | any upstream/orchestration failure not covered above, including every H1 guard refusal, an unknown `fileState`, and an ambiguous post-write failure whose re-probe could not resolve it |
| 502 | `{ code: "PARSER_MISMATCH" }` | update-service derived a different sha1 or `MapProofHash` than website-backend computed while spooling |
| 401 | `{ error: "Invalid token" }` / *(empty)* | same two 401 shapes as the status route |
| *(no response)* | — | the client disconnected — at any point, including mid-body-read or mid-orchestration — and nothing it did was worth logging |

Appendix A.3's own list omits `PROOF_MISMATCH` and `TEMP_MAP_KEY_MISMATCH`, although §6.3 step 4 names
both; both are implemented exactly as §6.3 describes (a launcher not coded for them sees a generic error,
per the cross-repo ruling that accepted this gap rather than changing the frozen wire contract).

## Auth

Both routes carry `[BearerRequiresPlayerAuth]` (`WebApi/ActionFilters/BearerRequiresPlayerAuthFilter.cs`),
the one `IAsyncAuthorizationFilter` in this service — every other auth filter here is an
`IAsyncActionFilter`, which runs *after* model binding. An authorization filter runs before resource
filters, before model binding and before the action, so a missing or invalid bearer token is answered
with 401 (`{ error: "Invalid token" }`) before a single byte of a (potentially 256 MiB) upload body is
read. Token **lifetime is deliberately not validated** (`validateLifetime: false`), matching every other
non-admin token check in this service (`AuthSessionController.MintTicket`): an expired but
correctly-signed player token is accepted, not rejected.

Both actions also fail closed with a bare 401 if, for any reason, the filter ran but left no battleTag in
`HttpContext.Items` (defence in depth for a code path that should not exist in production; the ledgered
CROSS-REPO ruling is that Appendix A.4 lists no 401 for the status route, but the filter and this
fail-closed path both answer one anyway — a launcher expecting only 200/404/429/502 there must also
handle a 401).

## Concurrency and quotas

Four independent limits, all in-memory and per-process (none survives a restart; the owner accepted
this — see `TemporaryMapLimits.cs`):

1. **Per-battleTag hourly upload quota** — `UploadsPerHourPerBattleTag = 10`, on the existing
   `MintRateLimiter` singleton, spent only when a **new record** is about to be created (a dedupe or a
   restore never spends it). A race where an update-service 409 turns an upload into a dedupe can
   over-count by one; `MintRateLimiter` has no release, so this is accepted and documented in a code
   comment at the spend site.
2. **Per-battleTag pre-check quota** — `PrecheckPerBattleTagPerMinute = 60`, same limiter, a different key
   prefix and window; exhausting it answers a bare 429 on the status route only.
3. **Per-battleTag single in-flight upload** (D7) — at most one upload per battleTag at a time.
4. **Process-wide in-flight cap** — `MaxConcurrentUploads = 8` across every battleTag, bounding spooled
   temp disk to 8 × 256 MiB regardless of how many Battle.net accounts are used at once.

Limits 3 and 4 live together in one DI singleton, `TemporaryMapUploadGate` (not named in the original
plan's file list, added during implementation). The controller acquires a slot **after** the auth filter
and **before the first request-body byte is read**, so a spool is never started for an upload that will
be refused; it releases the slot in a `finally` once the service has returned, compensation time
included. The per-battleTag bound is checked first, then the process-wide one (the per-battleTag slot is
handed back if the process-wide one refuses). Both answer the same body:
`429 { code: "QUOTA_EXCEEDED", retryAfterSeconds: TemporaryMapLimits.ConcurrentUploadRetryAfterSeconds }`
(= 30), the pinned Appendix A.3 shape. `retryAfterSeconds` for the hourly quota is instead computed from
the limiter's actual remaining window, rounded up to whole seconds and clamped to `[1, int.MaxValue]`
*before* the integer cast, so a saturated or sub-second window never yields 0 or wraps.

Separately, `TemporaryMapFileKeyLock` (a per-fileKey in-process async mutex, not a quota) serialises same
fileKey uploads/restores/sweep operations — see "Orchestration" below — and is shared between the upload
service and the sweep. Like `MintRateLimiter` and the gate above, it is single-process: two
website-backend instances would not see each other's locks, in-flight slots or quota counters.

Once the bytes are stored, the request runs to its outcome uncancellable (see below); under an mm/us
outage the worst case is roughly ten minutes (client timeouts on the record write, the re-probe, then up
to three compensation retries with 1/2/4 s back-offs) holding one of the 8 global slots. This was accepted
by ruling: correctness over latency while uploads are failing anyway; new uploads meanwhile see 429 and
retry.

## Orchestration

`TemporaryMapUploadService.HandleUploadAsync` (§6.3), after the reader has spooled the body and computed
sha1 and `mapProof` in one streaming pass:

1. Compare the client's sha1 hint to the server-computed one → `SHA1_MISMATCH` on mismatch.
2. **Dedupe by sha1** (`GetTemporaryMapBySha1`): no record → create; `present` → return the existing
   record (`200`, no write, no quota spent); `deleted` → restore.
3. **Create** (new record): spend the hourly quota, build the server-generated fileKey
   (`TemporaryMapNaming.BuildFileKey`), store the bytes at update-service, verify update-service's derived
   sha1/`MapProofHash` against what was computed locally (→ `PARSER_MISMATCH` on mismatch), validate the
   capture (→ `INVALID_LAYOUT`), then create the matchmaking record.
4. **Restore** (existing but `deleted` record): **verify the proof before mutating anything**
   (`VerifyTemporaryMapProof` — this call writes nothing, so a wrong proof costs no state) →
   `PROOF_MISMATCH` if unrecognised, `TEMP_MAP_KEY_MISMATCH` if the verified proof names a different map
   id than the sha1 dedupe did, or if the record's stored path is not a well-formed §6.4 fileKey. Only
   once verified does it store the bytes at that exact path, verify digests, and flip the record with
   `MarkTemporaryMapFileRestored` (`file-restored`) — so `fileState` is never `present` without bytes
   behind it.

**The per-fileKey lock.** Every store, digest check, record write, re-probe and compensation for one
fileKey runs under `TemporaryMapFileKeyLock.AcquireAsync(fileKey, …)`, held from just before the
update-service store until compensation (if any) is fully resolved. This closes a race where two
uploads (or an upload and the sweep) of the same fileKey could otherwise interleave and delete bytes the
other has just stored — a `present` record with no bytes behind it that dedupe alone could never repair.
The lock is acquired with the caller's request token *before* anything is stored (so an aborted wait
leaves nothing to undo); once the bytes are stored, the rest of the orchestration for that fileKey runs
with `CancellationToken.None` and is not cancellable by a client disconnect.

**The H1 guard (fileKey suffix grinding).** The §6.4 fileKey's uniqueness suffix is only an 8-hex-digit
(32-bit) sha1 prefix, which an attacker can grind to collide with a known live map's name. If
update-service answers **409 Conflict** while creating or restoring, the service never deletes the
existing file at that path blindly:

1. Re-probe matchmaking **by sha1** — if a record is now known, that record wins (a dedupe/keep), no
   delete.
2. If sha1 is still unknown, probe matchmaking **by path** (`GetTemporaryMapByPath`) *before any delete*:
   - a strict `404 {}` (no record claims the path) → genuine stray file → delete it and retry the store
     once;
   - any record claiming the path → **no delete, no retry**, `502 UPSTREAM`, logged with both sha1s and
     the fileKey (never the proof);
   - any other by-path outcome (transport failure, contract violation) → `502 UPSTREAM`, no delete.

This guard applies identically to create and to restore. Its soundness rests on two assumptions
documented at the code (`ReplaceStrayFileAsync`, S-I1): matchmaking enforces a **unique index on
`gameMap.path`**, so at most one record ever claims a given path; and update-service's file identity is
**byte-exact and case-sensitive**, so a by-path answer is really about the file the 409 reported. If
either assumption is ever violated, the guard degrades safely (an extra 502, never a wrong delete).

**F-A: re-probe before any post-write compensating delete.** Once the bytes are stored, *any* failure of
the matchmaking create or file-restored call — including a status-less one such as a timeout or a
transport error — re-probes matchmaking by sha1 once (with `CancellationToken.None`) before deciding
whether to compensate, because matchmaking can commit the write and still answer an error (e.g. its own
post-insert refresh failing, or a proxy 5xx after the write landed). Outcomes:

- create: a record now present at *our* fileKey → keep the bytes, return it (`200 deduped`); present at a
  *different* path → compensate our fileKey (delete), return the other record (`200`); still unknown →
  compensate, `502 UPSTREAM`.
- restore: the record now `present` **at the same map id** → success (`200 restored`, no delete); still
  `deleted`, or `present` at a *different* map id → compensate, `502 UPSTREAM`.
- the re-probe call itself fails → **no compensation**, `502 UPSTREAM` + a warning; the bytes are left for
  the reconciliation sweep to reclaim later (or a later restore, if a `deleted` record still claims the
  path).

A digest mismatch (`PARSER_MISMATCH`) or a failed capture validation (`INVALID_LAYOUT`) still compensates
directly — those are wb-side verification failures with a definite answer, not an ambiguous upstream one.

**What never compensates.** A failure **during** the update-service upload call itself (client abort,
HttpClient timeout, transport error) never triggers a delete: whether the bytes landed at all is unknown,
and deleting at that fileKey could remove another live file entirely (the H1 threat again). A genuine
orphan from this path is reclaimed only by the reconciliation sweep, after `OrphanMinAgeHours` (24h).

**F-B: the post-stray-delete retry is uncancellable.** After the H1 guard deletes a genuine stray file and
retries the store, that retry runs with `CancellationToken.None` — a client disconnecting right then must
not leave the fileKey permanently unusable.

## Validation

- **Capture** (new-map path only; a restore never re-validates it — the first uploader's layout is
  authoritative for every later host of that sha1): `lobbyMode` must be `"free"` (zero forces) or
  `"mapped-forces"` (at least one); `slotCount` 1..12 for a 12-player map, else 1..24; `maxTeams` 1..24;
  every force's team unique and below 24 (the observer "team"); every seat (human slot or computer),
  across all forces, a unique index below `slotCount`; every colour in `[0, 24)`; a computer's race one of
  Random/Human/Orc/Night Elf/Undead; a computer's difficulty one of Easy/Normal/Insane. A null
  `computers` list is forwarded to matchmaking as an empty one (matchmaking treats a missing list as
  "unknown", not "none"). Anything outside these ranges → `400 INVALID_LAYOUT`.
- **`originalFileName`**: at most 255 UTF-16 code units (no real file system hands out anything longer);
  over that → `400 METADATA`. The value forwarded to matchmaking has C0 and C1 control characters
  (U+0000–U+001F, U+007F–U+009F) stripped and nothing else changed.
- **Extension**: `.w3x`/`.w3m`, case-insensitive; anything else → `400 EXTENSION`.
- **fileKey naming** (`TemporaryMapNaming`, §6.4): `W3Champions/CustomGames/<sanitised name>-<sha1_8><ext>`.
  Sanitisation strips the extension, NFC-normalises, removes control characters and the reserved
  characters `<>:"/\|?*`, trims/collapses whitespace, and truncates to 100 characters — but it does
  **not** strip other Unicode format characters such as U+FEFF (zero-width no-break space), which
  therefore can appear (invisibly) inside a served path. This is a deliberate spec-faithful choice, not an
  oversight: website-backend is the only producer of a fileKey, so tightening this later is safe should
  it ever matter.

## Sweep and admin job

`TemporaryMapExpirySweep.RunOnceAsync(nowUtc, ct)` runs two passes, serialised behind one lock
(`SemaphoreSlim(1,1)`, no skip — the daily trigger and an admin-triggered run queue behind each other
rather than double-processing), preceded by a spool cleanup:

1. **Spool purge.** Deletes any file left in `TempUploadDir` (the crash-recovery case: a live upload
   writes to its spool file continuously and finishes within minutes) whose last-write time is older than
   `StaleSpoolFileAgeHours` (24h). Refuses to touch a directory that is a symlink, or whose owner-only
   permissions cannot be enforced (chmod 0700 where the OS has Unix modes) — this rule is shared with the
   upload reader. Runs first, so an upstream outage never delays it; per-file isolated, so one bad file
   never blocks the rest.
2. **Expiry pass.** Lists matchmaking records whose last game start is older than `TtlDays` (30) in
   batches of `SweepBatchSize` (200; the listing has no cursor — a full batch that made progress is
   re-listed, since deleting/marking shrinks what the next listing returns). For each: **delete the bytes
   at update-service first, then mark the record deleted** — this order matters, because a record marked
   deleted while its bytes survive would be an orphan nobody could ever reclaim by id, whereas bytes
   deleted before the mark are simply retried on failure. Every item is attempted at most once per run.
   Both the delete and the mark for one fileKey run under the same per-fileKey lock the upload service
   uses, so the sweep can never take bytes an in-flight upload just stored.
3. **Reconciliation pass.** Lists update-service's stored `W3Champions/CustomGames/` files at least
   `OrphanMinAgeHours` (24h) old (so a genuinely in-flight upload is never a candidate) and follows the
   listing to its end — there is **no page cap** (a fixed cap would silently stop examining files past
   some point while logging progress, which the "never drop failed work" rule forbids); only a
   non-advancing/repeated cursor, or a page that repeats only rows already scanned this run, ends the pass
   early (logged as a failure, retried by the next run). For each file: **reclaimed** (the bytes are
   deleted) when no matchmaking record claims the path (a strict `404 {}` by-path answer) or when the
   record that does claim it says `fileState: deleted` **and names this very path**; kept when the
   claiming record is `present`; anything else (another fileState, a `deleted` record naming a *different*
   path, a non-empty-body 404, a transport failure) is that file's failure — no delete, retried next run.
   The probe and the delete for one file also run under the per-fileKey lock.

Both passes never give up on a failed item: the owner's rule is that failed work is retried on the next
run, forever — there is no attempt budget and nothing is ever dead-lettered. Every per-item failure is
logged (at Warning or Error, depending on cause) and counted in the run's `Failed` total.

**Triggers.** `TemporaryMapExpiryService` (a `BackgroundService`) runs one sweep at host start (which also
purges stale spool files immediately), then one every `SweepIntervalHours` (24h). The same sweep is also
exposed as admin job `temporary-maps-expiry` (`EPermission.Maps`, and `EPermission.Jobs` to reach the
admin-jobs API at all), triggered with `POST /api/admin/jobs/temporary-maps-expiry/run`, for operators who
need it run now rather than at the next tick. Because both triggers share the sweep's own run lock, a
manual run queues behind an in-progress daily run rather than double-processing.

The admin job runner has **no separate failure column** — `IAdminJobContext.Report(current, total,
message)`'s `total` is "zero if unknown", not a failure count. The job reports
`items = scanned + deleted` and puts everything else in the message:
`scanned=<n> deleted=<n> reclaimedOrphans=<n> purgedSpoolFiles=<n> failed=<n>`. The sweep's own per-item
failures are also each logged individually as the run happens.

**Sweep summary log line** (Information, once per run):
`Temporary map sweep finished: scanned={Scanned} deleted={Deleted} reclaimedOrphans={ReclaimedOrphans} failed={Failed} purgedSpoolFiles={PurgedSpoolFiles}`.
A reclaim is additionally logged at Information as `ORPHAN_RECLAIMED {FileKey}: no temporary map claims
it` (unclaimed path) or `ORPHAN_RECLAIMED {FileKey}: temporary map {MapId} says its file is deleted`
(claimed by a `deleted` record). An orphan the *compensation* loop could not clean up immediately (a
failed create/restore whose delete retries all failed) is logged as `ORPHAN temporary map file left in
update-service at {FileKey}` (Warning) at the time it happens — the next reconciliation pass, at least 24h
later, reclaims it through the strict by-path check above.

No metrics are emitted for the sweep — the summary log line is the operational signal.

## Metrics

`website_temporary_map_uploads_total{result}` (a Prometheus counter, §13), incremented exactly once per
upload attempt that reaches a terminal outcome — a client abort is never counted. `result` is one of:

| Label | Meaning |
|---|---|
| `created` | a brand-new record |
| `deduped` | matched an existing `present` record by sha1 |
| `restored` | successfully restored an expired record |
| `rejected` | a client-caused 4xx: an Appendix A.3 rejection, an unreadable body, Kestrel's own request errors, or a refused in-flight slot (gate refusal — both the per-battleTag and the process-wide bound count here, since a 429 is the same class of outcome as any other client-caused rejection) |
| `upstream_error` | an upstream/orchestration fault answered with a 5xx A.3 body (`UPSTREAM`, `PARSER_MISMATCH`, `TEMP_MAP_KEY_MISMATCH`) |
| `server_error` | a fault of this service itself, answered as a bare 500 (a local spool/disk fault, a body-stream fault, or a cancellation the request did not cause) |

`server_error` was added during implementation (it is not named in Appendix §13, which pins only the
metric's existence — label values are not part of the frozen wire contract) to keep dashboards honest
about the difference between "our disk" and "their service".

## Telemetry and logging redaction

- `mapProof` and `proofHash` (and update-service's `MapProofHash`) are **never logged, anywhere, under
  any circumstance** (§10.3). Every log line that must report a failure from a proof-carrying matchmaking
  call (create, file-restored, verify-proof) logs only the exception type and the upstream HTTP status,
  never the exception's message — matchmaking's own error bodies can echo the request text back, which
  for those calls includes the proof.
- The inbound `proofHash` query parameter on the status route carries `[NoTrace]`, so it is excluded from
  OpenTelemetry/tracing spans and from Application Insights (this redaction was built in Task 4, ahead of
  this controller).
- Every other value an upstream service supplies and that might reach a log line (a `fileState`, a sha1, a
  stored path, a launcher version string, a spool file name) is passed through a shape gate first
  (`LoggableFileState`, `LoggableSha1`, `LoggablePath`, `LauncherVersionForLog`, `LoggableSpoolFileName`)
  that renders it as the literal `"invalid"` unless it matches the exact shape this service expects. This
  exists because the file log sink renders string properties raw, so an untrusted string could otherwise
  forge additional log lines (a pre-existing, repo-wide `W3CLoggerConfiguration` property; this feature
  limits its own exposure to it rather than fixing the sink).
- The admin passthrough's `uploadedBy` value (a battleTag, which may contain `#`) is percent-encoded
  exactly once on the update-service query string, so a `#` cannot truncate it.

## Deployment prerequisites (owned by other workstreams)

- **nginx-proxy** must raise `client_max_body_size` to `257M` before website-backend is deployed. website-
  backend's own per-action ceiling (`[TemporaryMapUploadBodyLimit]`, raised only on the upload action, not
  globally — the global Kestrel limit elsewhere in this service stays 128 MiB) is
  `TransportBodyBytes = 269_484_032` bytes (256 MiB file cap + 1 MiB of multipart/header slack). All three
  numbers (nginx, wb, update-service's own `[RequestSizeLimit]`) must agree; they are not derived from one
  shared constant, so a future change to any one of them must update the others by hand.
- **update-service** must accept `uploadedBy` on the admin map-file passthrough as a **query parameter**
  (not only a form field) and must return `MapProofHash` to `x-admin-secret` callers — these are
  prerequisites of the admin-passthrough path this service already forwards, not something website-backend
  builds.
- **matchmaking-service** must have the `/maps/temporary/*` admin routes and the `includeTemporary`
  listing filter live before website-backend's routes will work end to end (see "Deploy order" below).

## `GET api/maps` and `includeTemporary`

`MapsController.GetMaps` now sends `x-admin-secret` on **every** call, unconditionally — not only when
`includeTemporary` is requested — because matchmaking only honours `includeTemporary` for admin-secret
callers and never falls open otherwise. Removing the header would make the admin page's "Show temporary
maps" checkbox a silent no-op rather than an error. Pinned by
`WC3ChampionsStatisticService.Tests.Maps.TemporaryMapClientTests.GetMaps_SendsTheAdminSecretAndIncludeTemporary`,
`…TemporaryMapClientTests.GetMaps_SendsTheAdminSecretEvenForAPermanentOnlyListing` and
`…MapsControllerPassthroughTests.GetMaps_ForwardsIncludeTemporaryToMatchmaking`.

## Proof rotation (a documented recovery, not a job in this service)

If matchmaking's stored map records were ever to leak, the recovery is a manual, cross-repo operator
runbook, not a route or a scheduled job here: one release bumps `MapProof.Prefix` in this service (and in
launcher-e and update-service) from `w3champions-map-proof-v1\n` to `w3champions-map-proof-v2\n`; an
operator then triggers update-service's rotation route (`x-admin-secret`, paged) and relays each
`{ path, mapProof, proofHash }` triple it returns to matchmaking's own rotation route over the same
`x-admin-secret` channel; clients simply recompute proofs with the new prefix on next use. website-backend
deliberately has **no** job or route for this: a path that may never run in this service's lifetime is not
worth its own paging, retry and audit story here. The `mapProof` values that flow through that manual
relay are secrets — keep them out of shell history and logs. The runbook itself is documented in
update-service's and matchmaking-service's own operator docs, not duplicated here.

## No feature flag, no kill switch, no new configuration

There is no feature flag and no runtime toggle anywhere in this feature (owner decision) — the only
operational levers are the admin job above and the routes themselves. No new environment variables were
introduced in any service; website-backend reuses the existing `ADMIN_SECRET`, `MATCHMAKING_API` and
`UPDATE_API`.

## Deploy order

matchmaking hardening → docker-compose-files (nginx `client_max_body_size 257M`) → update-service →
matchmaking-service → **website-backend** → website → flo/launcher release.

Both out-of-order failure modes are safe and loud, and neither writes any data:

- Deploying website-backend **before update-service**: uploads fail with `502 PARSER_MISMATCH` (update-
  service has no `MapProofHash` to return to an admin-secret caller yet).
- Deploying website-backend **before matchmaking-service**: matchmaking has no `/maps/temporary/*` routes
  yet, so every call gets a route-level (non-empty-body) 404 rather than the pinned `404 {}` "no record"
  answer. The client-side contract treats that as a violation and throws, which every caller maps to a
  bare `502` (status route) or `502 UPSTREAM` (upload route) — every temporary-map request fails loudly
  rather than silently doing nothing or writing partial state.
