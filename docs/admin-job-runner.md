# Admin job runner — spec

Status: proposed, not built. Written after the lifetime-stats work, which needs
the first job.

## Why

Operational one-offs currently need shell access to the environment, which not
everyone maintaining the site has. The immediate need is a backfill of the MMR
timeline read model (below), but the same problem recurs — there is already a
`W3ChampionsStatisticService.Tools/MapMetadataBackfill` command that has to be
run out of band.

A job triggered from the admin UI also gets progress, an audit trail, and a
record of having been run, which a CLI invocation does not.

Scope is a job **runner**, not a scheduler. No cron, no retry/backoff, no
chaining, no arbitrary parameters, no log streaming. Each is a reasonable
follow-up; none is needed for the backfill, and together they are the difference
between a contained feature and a framework.

## What this builds on

Nothing here is novel infrastructure; it is mostly assembly.

| Piece | Where |
| --- | --- |
| Background services | `Program.cs` registers three via `AddHostedService` |
| Permission filter | `WebApi/ActionFilters/BearerHasPermissionFilter.cs` — also injects the caller into `context.ActionArguments["battleTag"]`, so an action taking a `battleTag` parameter receives the acting admin |
| Audit log | `IAuditLogRepository.Create(AuditLogEntry)`; entry carries `AdminBattleTag`, `Action`, `Category`, `EntityType`, `EntityId`, `Reason` |
| Mongo repositories | `MongoDbRepositoryBase`, plus `IRequiresIndexes` for index declaration |
| Admin menu | `AdminNavigation.vue` renders from a `navItems` array of `{ title, icon, permission, component, routeName }` |

## Single instance

**The service runs as one instance.** That is assumed throughout and is what
keeps this small. Specifically it means:

- No polling loop. The API starts the job in-process; there is no second replica
  that needs to discover queued work. (Polling loops do exist in this codebase —
  `ReadModelBase/AsyncServiceBase.cs` polls every 5s — but those consume a
  continuous event stream, which is a different problem.)
- No heartbeat, no stale-reclaim window, no owner/instance id. Those exist only
  to answer "is the other replica still alive?".
- Crash recovery is simpler and *more* reliable than a heartbeat: any job still
  marked `Running` at startup is definitionally dead, so the runner marks it
  `Interrupted` on boot and its checkpoint lets it resume.

The one place this is baked in is that startup sweep. With a second replica,
a booting replica would mark a job legitimately running elsewhere as
interrupted. It should carry a comment saying so, so that scaling out is a
conscious change rather than a silent breakage.

The status transition should still be an atomic `FindOneAndUpdate` on `Status`
rather than read-then-write. It is one line, it guards two admins clicking at
once, and it fails safe rather than double-running if the assumption ever
changes.

## Data model

One document per job key in an `AdminJob` collection — current state plus a
summary of the most recent run. A re-run overwrites the previous run's details;
there is deliberately no history (see "Why no run-history collection" below).

```
_id              string   job key, e.g. "timeline-backfill"
Status           enum     Idle | Pending | Running | Completed | Failed | Cancelled | Interrupted
Progress         { Current, Total, Message }
Checkpoint       BsonDocument   job-defined resume point
StartedAt        DateTimeOffset?
FinishedAt       DateTimeOffset?   set on every terminal status, not just Completed
Duration         TimeSpan?         FinishedAt - StartedAt, denormalised for display
ItemsProcessed   long              job-defined unit, carried across resumes
TriggeredBy      string   admin battleTag
RunCount         int
Error            string
```

`StartedAt`/`Duration`/`ItemsProcessed` describe the latest run only. On a
resume (`Interrupted` -> `Running`) they continue accumulating rather than
resetting, so a job that survived a deploy still reports its true total; a
`reset` or `force` re-run clears them.

### Why no run-history collection

The obvious alternative is to reuse `IAuditLogRepository` for per-run history.
Worth knowing before choosing that: **the audit log is currently write-only.**
`IAuditLogRepository` exposes `GetRecent`, `GetByAdmin`, `GetByAffectedUser`,
`GetByCategory` and `GetByEntity`, and nothing in the solution calls any of
them. The writers are the rewards controllers and `ApiTokenController`; there is
no admin page and no API endpoint reading it back, and no TTL index, so entries
accumulate and are only reachable by querying Mongo directly.

So the audit log is the right place to record *that an admin triggered a job*
(accountability, consistent with how other admin actions are recorded), but not
a practical place to read run outcomes from — a reader would have to be built
from scratch either way. Keeping last-run info on the job document means the
existing `GET /api/admin/jobs` response already carries everything the UI needs.

If run-over-run history is wanted later, a separate `AdminJobRun` collection can
be added without changing this document's shape; the fields above become a
denormalised copy of the latest run.

## Runner

A singleton `AdminJobRunner`, registered as both a singleton and a hosted
service. The hosted-service half only sweeps interrupted jobs on startup and
cancels on shutdown; the API drives everything else directly.

```csharp
runner.TryStart(key, battleTag, force, reset)   // false if already running
runner.Cancel(key)                              // in-process CancellationTokenSource
```

### Back-pressure

Jobs call `context.Pace()` between batches. Rather than holding them to a fixed
share of wall clock, this reads whether the system actually has headroom and
runs flat out when it does, doubling the pause while pressure persists and
halving it once things recover.

Two signals, sampled once a second:

- **Database.** This is a single mongod - no transactions or change streams
  anywhere in the solution, both of which need a replica set, and every
  connection string is single-host - so there is no replication lag to watch.
  The equivalent is WiredTiger eviction pressure: replica lag is really a proxy
  for "writes are piling up faster than they can be durably absorbed", and on a
  standalone server that shows up as a growing dirty cache and, past a point, as
  mongod conscripting query threads into eviction. The two counters
  (`number of times dirty trigger was reached`,
  `application threads page write from cache to disk count`) need no threshold -
  any movement is the server complaining. Dirty-cache fraction, write-ticket
  utilisation and queued writers are gauges and do have thresholds, which are
  named constants and want tuning against real load.
- **CPU.** Jobs run in the same process as the API, so a job that pegs the CPU
  degrades every request the site serves even when the database is happy.

`FallbackDutyCycle` (25%) applies only when `serverStatus` cannot be read -
most likely a user without `clusterMonitor`. Running flat out blind is the one
genuinely dangerous option, so that case pays the fixed pace.

The WiredTiger statistic names are not a stable API and have moved between
releases; if they move again the probe reports no pressure and jobs run flat
out. `PressureProbeTests` asserts against a real server rather than a fixture
for exactly that reason.

Two further implementation notes that are easy to get wrong:

- **The job runs in a DI scope the runner creates**, not the request's. Scoped
  services are disposed when the HTTP response completes, so a job inheriting
  the request scope dies partway with a confusing `ObjectDisposedException`.
- **Progress writes are throttled inside the context**, not by each job. A job
  reporting per record should not spend more time writing progress than working.

## Job contract

Discovered from DI the same way `IRequiresIndexes` implementations are.

```csharp
public interface IAdminJob
{
    string Key { get; }
    string Name { get; }
    string Description { get; }
    EPermission RequiredPermission { get; }   // defaults to Jobs
    bool RequiresConfirmation { get; }        // UI demands the job name be typed
    Task RunAsync(IAdminJobContext context, CancellationToken cancellationToken);
}

public interface IAdminJobContext
{
    BsonDocument Checkpoint { get; }
    Task Report(int current, int total, string message, BsonDocument checkpoint = null);
}
```

## API

On the admin controller, all behind `[BearerHasPermissionFilter(Permission = EPermission.Jobs)]`:

```
GET    /api/admin/jobs                 definitions + current state
POST   /api/admin/jobs/{key}/run       ?force=true  ?reset=true
POST   /api/admin/jobs/{key}/cancel
```

`run` returns 409 when the job is already running, or when it has completed and
`force` was not passed. `reset` clears the checkpoint so a resumable job starts
over. Every trigger writes an audit entry with `Category = "JOB"` and
`EntityId = key`.

A job's own `RequiredPermission` is checked inside the action, since the filter
attribute is static.

## Permission

`EPermission.Jobs = 12`. The enum is mirrored by hand and must be added in
**both** places:

- `W3C.Contracts/Admin/Permission/Permission.cs`
- `../website/src/store/admin/permission/types.ts`

## Frontend

`AdminJobs.vue` plus one `navItems` entry gated on `EPermission.Jobs`. A table
of jobs with status chip, progress bar, last run and Run/Cancel buttons, polling
the list every ~2s while anything is running and every ~15s otherwise. That is
UI refresh, unrelated to the runner. SSE is not worth it for a handful of rows.

Jobs with `RequiresConfirmation` open a dialog requiring the job name to be
typed.

## First job: timeline backfill

Populates `Rd`, `Games` and `DailyMaxMmr` on historical `PlayerMmrRpTimeline`
entries and raises their `SchemaVersion`, so the lifetime endpoint can report a
calibration-aware peak. Until it runs, peaks are withheld for every player whose
history predates those fields.

The source is the retained `MatchFinishedEvent` collection, read directly - not
the read-model pipeline, and not `Matchup`.

That collection goes back to the beginning: it appears in the second day of the
repository's history (`569f39c`, "saving of raw data working") as the raw store
the read models were built on, it has no TTL index, nothing deletes from it, and
`MatchEventRepository.InsertIfNotExisting` exists to *fill in* events synced from
the matchmaking service. `Matchup` is derived from these events, so it cannot
have deeper coverage. Worth confirming against prod once by comparing the oldest
`_id` in each collection.

`Matchup` looked like the obvious source but is subtly wrong for this:
`PlayerOverviewMatches.OldRankDeviation` is the RD *before* the match
(`Matchup.cs` sets it from `w.mmr.rd`), whereas the live handler stores the RD
*after* (`player.updatedMmr.rd`). Backfilling from `Matchup` would therefore
mark players as calibrating for slightly longer than live-written entries do,
leaving a systematic seam between backfilled and live history. The events carry
`updatedMmr.rating`, `updatedMmr.rd` and `ranking.rp` - exactly the handler's
inputs - so a rebuilt entry is what the handler would have produced. The
handler's exclusions (arranged teams, missing `updatedMmr`) are applied for the
same reason.

Events are bulkier than `Matchup` documents, which matters when reading all of
match history, so the query projects down to the five fields used.

**Batch by day, not by season.** A season is ~1M matches and ~15k active
players; holding a season's timelines in memory is a few hundred MB per mode. A
day is ~6k matches and ~4k players, which is flat regardless of how much history
is processed.

**Range by `_id`, not by date.** There is no index on `EndTime`, but `Matchup.Id`
is an `ObjectId`, whose leading bytes are a creation timestamp — so a day's
matches can be selected with `_id >= ObjectId(day) && _id < ObjectId(day + 1)`,
served by the always-present `_id` index. `MatchRepository.LoadFor` already
relies on this ordering when it sorts by `_id` descending.

There is deliberately **no index on `Teams.Players.BattleTag`**, and this design
avoids needing one: it never queries by player, it reads every match once in
order. Adding that index would otherwise stall startup, because
`MongoIndexInitializationService` awaits `EnsureIndexesAsync` during boot and
`createIndexes` does not return until the build completes.

Other properties:

- **Checkpoint** is the next day to do; progress is days done / total.
- **Idempotent per day.** Insertion order approximates but does not equal
  `EndTime`, so the `_id` window is padded by two hours at each end and the
  exact day selected on `EndTime`. A day's existing entries are removed before
  the rebuilt one is inserted, so a rerun converges rather than double-counting.
- **Stops at yesterday**, relative to whenever it runs. Today is still being
  written by the live handler, and rewriting a day underneath it would drop
  games that land mid-run.

  **Run it on a later UTC day than the timeline handler was deployed on.** The
  run day is never rebuilt, so on the deploy day the entries the *old* handler
  wrote that morning stay as they are while the document is still promoted to
  the current schema version - claiming games counts and intra-day peaks that
  were never computed. Any later day is fine; no need to wait beyond that.
- **Throttle** via `context.Pace()` between days - see Back-pressure above.
- Re-runnable, but requires `force` once completed.

`SchemaVersion` is raised in a single pass at the end rather than as each day is
written, because a document is only fully rebuilt once the run reaches the end -
stamping it earlier would claim the whole history had the new fields while its
later days still did not. A blanket update at the end would have the opposite
problem, sweeping in timelines the backfill never touched (a player whose match
events are missing), whose entries genuinely cannot be verified. So each
rewritten document is marked with `BackfillPending` as it goes and only marked
documents are promoted at the end.

Note the rebuild recomputes each series from matches under today's rules, so it
will not reproduce existing points exactly — the live handler skipped arranged
teams and matches without `updatedMmr`, and applied rules that have changed.
Some players' charts will visibly shift as a result.

**This is intended.** The backfill correcting existing points is a wanted side
effect, not just a tolerated one: the alternative (add the new fields, leave
existing `Mmr` values alone) would leave each timeline internally inconsistent,
with backfilled days computed one way and live-written days another. The job
should therefore rewrite each day it touches, not merge into it.

## Later jobs

**`season-reset`** — the reset lives in the matchmaking service, not here:
`POST /admin/reset` in `matchmaking-service/src/app/apis/admin.api.ts`, guarded
by `requireAdmin` plus a hardcoded secret in the body. `resetManager.resetSeason()`
loops every player and team entity with no progress output and no resumability.

A job here would trigger it through `MatchmakingServiceClient` (needs a method
adding) and poll `GET /admin/server-state` for `seasonIsResetting` to clear,
which `server-state.ts` already exposes. That yields a button, an audit trail
and a coarse running/finished state without touching the reset logic. Finer
progress would need matchmaking to report a counter.

Should be `RequiresConfirmation = true`, and arguably its own permission rather
than sharing `Jobs` with routine backfills.

**`create-index`** — adding a large index safely, out of band from startup, with
real progress read from Mongo's `currentOp`.

## Decisions taken

- **Run history**: last-run summary on the job document, no history collection.
  See "Why no run-history collection".
- **Chart shift from the backfill**: accepted, and treated as a fix. See the
  backfill section.

## Open questions

1. Which permission each job should require. Deliberately left open — because
   `RequiredPermission` is a property of the job rather than of the runner,
   individual jobs can be moved to a stricter permission later without touching
   the runner or the API. `season-reset` is the likeliest candidate for one of
   its own.
