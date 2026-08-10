# Cancelled Matches Admin Page — Design

**Date:** 2026-08-05
**Status:** Approved design, ready for implementation planning
**Repos affected:** `matchmaking-service`, `website-backend`, `website` (frontend)
**Permission gate:** `EPermission.Moderation` (existing — no new permission)

## Problem

Matches that end without a recorded result are invisible on the website. Only
completed, ranked matches reach the `Matchup` read model and the public
`/api/matches` surface. Moderators therefore have no way to review a cancelled
game — no participant list, no replay, no chat log — which is exactly the
population most likely to contain misbehaviour worth acting on.

The goal is an admin page listing cancelled matches for a given game mode,
reusing the existing match-history presentation, with per-row access to the
replay download and the chat log. There is no score screen, no winner/loser and
no MMR change, because none of that data exists for these matches.

## Background: how a match becomes cancelled

Understanding this chain matters, because it bounds what the page can show.

### The `-draw` vote is map-side

`-draw` is implemented in the ladder maps, not in any service:
`map-updater-scripts/src/player_features/draw.ts`. It registers a chat trigger
per playing slot and, once enough players have voted, calls
`RemovePlayerPreserveUnitsBJ(Player(i), PLAYER_GAME_RESULT_NEUTRAL, false)` for
every player.

Two constraints come from that file:

- The command is rejected after 120 seconds of gameplay
  (`getElapsedTime() > 120`).
- The vote threshold is `playerCount - 1` when there are 4 or more players,
  otherwise `playerCount` (unanimous).

Because every player leaves with a neutral result, **nobody wins**.

### No winner means no valid result, which means a timeout cancellation

`MatchResultLog.isValid` requires `hasWinner`
(`matchmaking-service/src/app/data/models/match-result-log.ts:21-23`), computed
as `result.players.find(p => p.won) !== undefined`. A drawn game therefore never
produces a valid result, never enters `winnerResultMap`, and
`getValidResult()` returns null indefinitely. The match sits in `STARTED` until
the 5-minute `cleanUpMatches` sweep cancels it for exceeding 6 hours
(`matches.manager.ts:296-301`).

**Consequence: a drawn match is not marked cancelled for roughly 6 hours.**
Accepted for v1; the page carries an explanatory note (see Frontend). Revisiting
this by plumbing the W3GS `LeaveDraw` reason
(`flo/crates/w3gs/src/protocol/constants.rs:188`, already decoded by flo's
observer edge) through to matchmaking is deferred.

### No cancellation reason is recorded on the match

`MatchesRepo.cancelMatch` (`matches.repo.ts:79-84`) sets `state = CANCELED` and
saves; it records no reason, and leaves `endTime` and `lengthSeconds` unset. A
rich `EGameCreationResult` enum (23 values) exists but is written only to match
logs and metrics, never onto the `Match` entity.

The reason is not discarded, though: `gameCreationFailed` passes it to
`sendGameCancelledDialog(cancellationData)` (`game-creation.flow.ts:255-257`),
and the launcher has translated strings for every one of the 23 values
(`launcher-e/src/translations/*.ts`, e.g. `"Game cancelled: PLAYER_CAUSED_JOINBUG"`).
So the reason exists and is already transmitted to clients at the moment of
failure — it is only never persisted. Adding it to the `Match` entity would be a
small change, should it be wanted later.

### `MatchLogs` exist but are not reachable from this backend

There is a per-match event log: collection `MatchLogs`
(`matchmaking-service/src/app/data/models/match-event-log.ts:23-24`), keyed by
`matchId`, written via `MatchCreationLogsRepo.findOrCreateMatchLog`. It captures
the whole creation state machine — system events, per-player game-creation
events, host commands, client-submitted events — including the explicit
`game creation failure reason: <EGameCreationResult>` line
(`game-creation.flow.ts:253`).

It is not exposed over HTTP: no route in `src/app/apis/` references match logs.

Deliberately **out of scope for v1**, per review: the logs are not intended for
human consumption and carry a very large volume per match. Reconciling them
against the cancelled-match list is a plausible follow-up, and the join key
(`matchId`) is on every row this design returns, so nothing here blocks it.

### There are four `EMatchState` values and no draw state

`INIT`, `STARTED`, `FINISHED`, `CANCELED` (`match.ts:44-49`). A draw is
indistinguishable from "nobody reported a win".

### Every path that can cancel a match, and where each is recorded

This was challenged in review, re-verified exhaustively, and the conclusion
changed the design. The critical distinction is between **the `Match` collection**
(matchmaking's own store) and **the `MatchCanceledEvent` stream** (what the
statistic service consumes).

`MatchCanceledEvent` has exactly one producer and one call site:

- `grep -rn "pushMatchCanceled" src/` → the definition at `stats-hook.ts:106`
  and a single call at `matches.manager.ts:319`.
- `cancelMatch` has two call sites: `matches.manager.ts:168` (creation failure,
  **no** push) and `:317` (the sweeper, followed by the push at `:319`).
- No other repository writes the event collection: grepping `MatchCanceledEvent`
  and `W3Champions-Statistic-Service` across `flo`, `launcher-e` and `w3c-replay`
  returns nothing. The launcher only *receives* cancellations — its
  `cancelMatchmaking` (`backend.service.ts:290`) cancels queue search, not a
  match, and clients never report a match as cancelled.

**But `cancelMatch` always persists.** It calls `this.save(match)`
(`matches.repo.ts:79-84`) on *both* paths, and the match document already exists:
`createMatch` → `insertWithId` runs the instant the queue pairs players
(`match-maker.ts:605`), before `gameCreationManager.addMatch` starts the creation
flow. So every match is in Mongo at `INIT`, and every cancellation is written
there regardless of which path took it.

| Path | In `Match` collection | Emits `MatchCanceledEvent` |
| --- | --- | --- |
| `INIT` older than 2 min (sweeper) | **Yes** | Yes, ladder only |
| `STARTED` older than 6 h (sweeper — the draw bucket) | **Yes** | Yes, ladder only |
| Game-creation failure (flo error, join bug, disbanded team, leaver, per-state timeouts) | **Yes** | **No** |
| Tournament cancellations | **Yes** | No — ladder-gated |
| Custom matches | **Yes** | No — ladder-gated |
| 1v1 disconnect-forfeit | Yes, as `FINISHED` with 0-second duration | No |
| `FINISHED` older than 6 h | Yes, unchanged | No — evicted from memory only |
| Client/launcher report | Does not exist | Does not exist |

**The `Match` collection is the complete source of truth; the event stream is a
lossy projection of it.** This design therefore reads the former, not the latter.

Two details that make the event stream unusable as the primary source:

1. **A failed creation emits no `MatchStartedEvent` either.**
   `statsHook.pushMatchStart` is called only from `gameCreationSuccess()`
   (`game-creation.flow.ts:175`). So those matches are entirely invisible to the
   statistic service — no record of the attempt at all. That is precisely the
   data wanted for match-creation abuse scenarios.
2. **The sweeper cannot pick them up as a fallback.**
   `decideMatchOutcomeDuringFailedGameCreation` calls `removeCurrentMatch` at
   `:169`, evicting the match from the in-memory map that `cleanUpMatches`
   iterates. And `processLeavers` calls that method **unconditionally**, even when
   the leavers array is empty (`game-creation.flow.ts:524-531`), so every creation
   failure takes this path.

For completeness, two hypotheses raised in review that the code does not support:
there is no "joining a new game cancels the old one" path (`latestMatch` only
routes a disconnect to the right in-flight creation flow,
`game-creation.manager.ts:79-87`), and matches are rehydrated from the database on
startup for any non-`FINISHED` state (`matches.manager.ts:26-32`), so a process
restart does not orphan `STARTED` matches.

## Scope

**In scope:** all cancelled matches recorded in matchmaking's `Match` collection —
including game-creation failures, which no event ever reported. Reading the
collection directly means coverage is complete from day one with no backfill.

Also in scope, as a drive-by fix: **adding `GM_FOOTMEN_FRENZY` to
`GameModesHelper.FfaGameModes`**. Its matchmaking-service definition sets
`isAnonymous = true`, so flo masks names in-game, but the backend was not treating
it as an FFA mode — meaning ongoing Footmen Frenzy matches leaked real battletags
through `/api/matches/ongoing` and were lookupable by player. Anon-FFA Footmen
Frenzy is returning, so this needs to be correct. The three call sites
(`MatchesController.cs:201`, `PlayersObfuscator.cs:12`, `Matchup.cs:160`) are all
anonymization behaviours. `GM_LTW_FFA` remains correctly absent —
`gm-ltw-ffa.ts:30` sets `isAnonymous = false`.

**Out of scope, to be raised at PR review:**

1. **The missing `pushMatchCanceled`/`pushMatchStart` on the creation-failure
   path.** This design no longer depends on it, since it reads the `Match`
   collection instead. It remains a real inconsistency worth fixing on its own
   merit, so that other read models and future event consumers see these matches.
2. **`LeaveDraw` is not treated as a first-class outcome** — would remove the ~6h
   delay before a drawn match is marked cancelled.
3. **Recording the cancellation reason** on the `Match` entity — see above; the
   reason already exists at failure time and is sent to clients.
4. **Displaying `MatchLogs`** alongside each cancelled match.

## Design

### Data source: proxy matchmaking-service

Rather than building a `CanceledMatch` read model fed by the event stream, the
statistic service proxies a new matchmaking-service admin endpoint. This is the
established pattern for admin data that matchmaking owns — banned players, player
warnings, warning definitions, queue snapshots, maps and MOTD all work this way
via `MatchmakingServiceClient`.

Why this over a read model:

- **Complete.** Includes creation-failure cancellations, which no event carries.
- **No backfill, full history immediately.** Matchmaking has no TTL on `Match`
  (its only TTL is `ip-intel-cache`), so every historical cancellation is already
  there.
- **No `HandlerVersions`, no replay-from-`ObjectId.Empty`, no `START_HANDLERS`
  dependency**, and no duplicate-suppression logic.
- **Self-healing.** `healMatches` can retroactively flip a cancelled ladder match
  to `FINISHED` up to 2 days later (`matches.manager.ts:326-357`). With a live
  query the row simply stops matching `state = CANCELED` and disappears. A read
  model would have needed a removal handler to avoid phantom rows.
- Matchmaking keeps ownership of its own data.

The cost is a runtime dependency on matchmaking being reachable, which the admin
area already has for several other pages.

### 1. `matchmaking-service` changes

**New admin endpoint**, following the `/warnings` shape exactly
(`admin.api.ts:55-73`):

```
GET /admin/canceled-matches?page=&itemsPerPage=&gameMode=&battleTag=
    -> { total, matches }
```

- Guarded with `requireAdmin`, like every other admin route.
- `page` is 1-based, default 1; `itemsPerPage` default 25, clamped to a sane
  maximum. Offset is `page * itemsPerPage - itemsPerPage`
  (`admin.api.ts:493`).
- Filter `state: EMatchState.CANCELED`, optional `gameMode`, optional
  `players.battleTag`.
- Sort `_updated_at` descending (see below).
- Add it to `MatchesRepo` as a properly paged query. The existing
  `getCancelledMatchesSince(date)` (`matches.repo.ts:34-41`) is unpaged and capped
  at 100k documents, so it is not reusable as-is; it can stay for its current
  caller.
- **Query Mongo, not the in-memory cache.** `MatchesRepo.initialize` loads only
  `getNotCanceledMatches()` into `this.matches`, and `cancelMatch` removes the
  match from it, so cancelled matches are never in the cache by construction.

**New index.** The collection currently has only `{_created_at: -1}` and
`{players.battleTag: 1}` (`matches.repo.ts:19-21`). Add a compound index
supporting the primary listing, e.g. `{state: 1, gameMode: 1, _updated_at: -1}`,
via the same `createIndex` calls in `initialize()`.

**Populate `IMatchPlayer.slotIndex` for matchmaking paths.** The field already
exists and is already documented as the canonical FLO slot index
(`match.ts:31-41`): *"Lobby slotId. For custom games this is the canonical FLO
slot index (0-based, matches `CreateGameSlot[]` index) … Optional for back-compat
with non-custom (matchmaking) paths where the slot is assigned later by
`setPlayersToSlots` via `getNextAvailableSlot`."*

So nothing new needs designing — only the write. In `setPlayersToSlots`
(`player-slots.helper.ts:14-29`), which already resolves each player's slot and
assigns `slot.player_id = x.playerInstance.floPlayer.id`, also set
`x.matchPlayer.slotIndex = slots.indexOf(slot)`.

It is **0-based**, matching the `CreateGameSlot[]` index. Flo's mask is
`format!("Player {}", i + 1)` and replay-service's `PlayerInfo.id` is likewise
`idx + 1`, so the UI must display `slotIndex + 1`.

This only applies to matches that reached flo game creation. `INIT` timeout
cancels never called `createGame`, so they have no slots — but they also have no
replay and no chat log, so there is nothing to reconcile against.

### 2. `website-backend` changes

**`MatchmakingServiceClient`**: add a request/response pair mirroring the existing
`PlayerWarningsGetRequest` / `PlayerWarningsResponse` convention
(`MatchmakingServiceClient.cs:759-770`):

```csharp
public class CanceledMatchesGetRequest
{
    public int Page { get; set; } = 1;
    public int ItemsPerPage { get; set; } = 25;
    public GameMode GameMode { get; set; }
    public string BattleTag { get; set; }
}

public class CanceledMatchesResponse
{
    public int total { get; set; }
    public List<Match> matches { get; set; }
}
```

**Reuse the existing `Match` DTO — do not write a new one.** The endpoint returns
matchmaking `Match` documents, which `W3C.Domain/MatchmakingService/MatchEventDtos.cs`
already models: `Match` (`:100-151`) with `id` (mapped from `_id`), `state`,
`gamename`, `map`/`mapId`/`mapName`, `gameMode`, `gateway`, `season`, `host`,
`startTime`, `endTime`, `floGameId`, `floNode`, `serverProvider`, `type`, and
`players` as `List<PlayerMMrChange>`, which already inherits from
`UnfinishedMatchPlayer` (`:24,44`). This is the "don't reinvent the wheel"
resolution: the inheritance chain exists and models exactly this document.

Three additions to those DTOs:

- `slotIndex` on `UnfinishedMatchPlayer`, matching the upstream field.
- `_created_at` and `_updated_at` on `Match`, which are `BaseEntity` fields
  (`base-entity.ts:11-13`) not currently mapped.

**New `AdminMatchesController`**, route prefix `api/admin/matches`. Every action
carries `[BearerHasPermissionFilter(Permission = EPermission.Moderation)]` — the
attribute is `AttributeTargets.Method`, so it cannot be applied at controller
level and must be repeated per action.

```
GET api/admin/matches/canceled?gameMode=&battleTag=&page=&itemsPerPage=
    -> { total, matches }
```

The `api/admin` prefix already sub-layers into `api/admin/permissions`,
`api/admin/logs`, `api/admin/storage` and `api/admin/api-tokens` (only
`AdminController` itself sits at the bare `api/admin`), so grouping under
`api/admin/matches` leaves room for further match-related admin endpoints.

Pass paging through to matchmaking rather than re-paging locally, and surface
`total` unchanged so the frontend can drive a pager.

Do **not** apply `PlayersObfuscator`. It exists for ongoing FFA matches and for
MMR/rank-deviation masking on public endpoints; this is a Moderation-gated admin
surface that needs real values.

**This endpoint must not call the replay service.** See the cost constraint under
FFA de-anonymization.

### Timestamps

`endTime` is never set on a cancellation and the C# `Match` DTO coerces the null
to `0` (`MatchEventDtos.cs:124-136`), so it is unusable.

Use the entity timestamps instead, which are real maintained fields rather than
inferred values:

- **`_created_at`** — when the match was formed by the queue. Exact.
- **`_updated_at`** — set by `EntityRepo.save` immediately before the
  `replaceOne` (`entity-repo.ts:64-66`), and `cancelMatch` goes through `save`.
  For a document still in state `CANCELED` this **is** the cancellation time,
  since nothing writes the document afterwards except a heal, which changes the
  state and removes it from this listing.

Expose `_updated_at` as `canceledAt`, and document that caveat on the field.
Sort by it descending.

An earlier draft proposed deriving a timestamp from the event `ObjectId`. That is
rejected: ObjectIds should not be inspected for their embedded timestamp, and the
value would have been "when the sweeper ran" — misleading by up to hours given a
6-hour threshold polled every 5 minutes.

### 3. Replay and chat log reachability

Both the replay download and the chat log bottom out in the **same single
identifier — the flo game id (int)**. There is no second id to be missing:
`ReplaysController` `{gameId}` and `{gameId}/chats` both call
`MatchRepository.GetFloIdFromId` and pass the resulting int to
`ReplayServiceClient`, whose two methods format that int into `/generate/{id}`
and `/chats/{id}`.

`GetFloIdFromId` reads the **`Matchup` collection only**
(`MatchRepository.cs:275-280`). A cancelled match has no `Matchup` row, so the
`{gameId}` routes can never serve one. Worse, `new ObjectId($"{gameId}")` at
`:277` is unguarded, so passing the matchmaking nanoid yields a 500 rather than a
clean 404.

**Therefore: no new replay or chat endpoints.** The list payload exposes
`floGameId`, and the frontend uses the existing `by-flo-id` routes, which do no DB
lookup and only reject `0`.

`floGameId` is nullable and sometimes genuinely absent: it is assigned only after
flo's `createGame` returns (`flo-game-creation.flow.ts:172-174`). Matches cancelled
before that — the 2-minute `INIT` sweep, and early creation failures — have none,
and **can never have a replay or chat log**. The UI must render those buttons as
unavailable rather than failing. Note that creation failures which got as far as
flo *do* have a `floGameId`, because it is assigned to the match object as soon as
`createGame` returns, specifically so the error path can cancel the flo game.

#### 3a. Raise the hourly replay rate limit for authenticated moderators

Cancelled matches always land in the **strict** bucket, because
`ReplayRateLimitAttribute.CheckMatchAge` resolves age via
`LoadFinishedMatchDetailsBy*`, which returns null for a cancelled match →
`isRecent == null` → strict (10/hour, 50/day). Ten downloads an hour is thin for a
moderator working through a list.

Change `ReplayRateLimitAttribute`:

- Add `ModeratorHourlyLimit { get; set; } = 50`.
- After `rateLimitService.DetermineRateLimitContext(...)` returns, if the request
  carries a valid bearer token whose permissions include `EPermission.Moderation`
  **and** `!ctx.HasValidApiToken`, set `ctx.HourlyLimit = ModeratorHourlyLimit`,
  `ctx.PolicyName = "replay-moderator"`, and
  `ctx.PartitionKey = $"moderator:{battleTag}:{Scope}"`.
- **Leave `DailyLimit` untouched** — it stays at whatever strict/relaxed resolved
  (50 strict, 100 relaxed).

Resolve the auth service from DI: `IW3CAuthenticationService` is registered at
`Program.cs:178` as an intercepted transient, so use
`RequestServices.GetRequiredService<IW3CAuthenticationService>()` rather than
`BearerHasPermissionFilter`'s `new W3CAuthenticationService()`.

Partitioning by battleTag rather than IP is deliberate: it keeps one moderator
from consuming another's budget behind a shared IP, and matches the existing key
conventions (`ip:{ip}:{scope}`, `token:{id}:{scope}`).

With hourly at 50 and strict daily at 50, the daily cap becomes the binding
constraint — a moderator can spend the whole day's allowance in one hour. That is
the intended trade-off: bursty review sessions are the actual usage pattern, and
the daily ceiling still holds. Unauthenticated and non-moderator behaviour is
unchanged.

#### 3b. Handle unavailable replays and chat logs properly

Both directions are currently wrong, and both must be fixed — a replay being
unavailable is a normal, expected outcome, not an error. It may never have been
recorded, or it may have aged into DeepArchive.

- **`GetChatLogs`** (`ReplayServiceClient.cs:29-33`) uses `GetAsync` +
  `ReadAsStringAsync` with **no status check**, so an upstream 404 deserializes to
  null and surfaces as HTTP 200 with a null body. Must map a non-success status to
  a clean 404.
- **`GenerateReplay`** (`:18-25`) has the opposite problem: `GetStreamAsync`
  applies `EnsureSuccessStatusCode` internally, so an upstream 404 becomes an
  unhandled exception → HTTP 500. Must map upstream 404 and the
  archived/unavailable case to a 404 the client can render as "replay
  unavailable".

The frontend already distinguishes 404 from other failures
(`DownloadReplayIcon.vue` has separate `notFound` and `unavailable` strings), so
correct status codes are immediately usable.

#### 3c. Never return `playerScores: []`

If any payload feeding `MatchDetail.vue` carries `playerScores: []` instead of
`null`, the page silently flips into "complete game" mode: `isCompleteGame`
(`MatchDetail.vue:303`) is typed `PlayerScore[]`, not boolean, and an empty array
is truthy. That suppresses the `incompletedata` message, renders an all-zeros
score screen, and unlocks the unguarded FFA dereferences at `:335` and `:487-499`.
`null` is correct and is what the existing code already produces.

## FFA de-anonymization

The requirement is to reconcile who is who in modes that hide names in-game. No
new mapping table is needed.

### Where "Player N" comes from

Not this backend. Flo masks names as exactly `format!("Player {}", i + 1)` where
`i` is the **slot index**, at `observer-edge/src/game/snapshot.rs:187-191` and
`observer-edge/src/game/mod.rs:173` among others. It is driven by
`game.mask_player_names`, which matchmaking sets from
`gameMode?.isAnonymous || match.hideCustomGamePlayerNames`
(`flo-game-creation.flow.ts:160`). `isAnonymous` is true for `GM_FFA`,
`GM_SC_FFA_4`, `GM_SC_OZ` and `GM_FOOTMEN_FRENZY`.

The backend's own FFA obfuscation is a separate mechanism that produces `"*"`,
not `Player N`, applies at API-response time only, and only to **ongoing** matches
(`PlayersObfuscator.cs:18-19`, called from `MatchesController.cs:190,206`).

### Both identity sources are already unmasked

1. **`Match.players[].battleTag` is real.** Matchmaking stores the live match
   object; it anonymizes nothing in its own collection. The proxied list therefore
   carries real battletags with no extra work.

2. **The chat log already resolves slot → real name.** replay-service's chats
   handler calls flo's gRPC `get_game`, and `Game::pack()`
   (`controller/src/game/types.rs:45-68`) packs `slots` verbatim **without
   masking** — masking lives only in the observer-edge, client and player-sender
   paths. The handler then builds `PlayerInfo { id: idx + 1, name: player.name }`
   over the same slot enumeration
   (`w3c-replay/binaries/replay-service/src/handlers/chats.rs:139-151`).

   Because both use `slots.iter().enumerate()`, **`chatLog.players[].id` is exactly
   the N in "Player N"**, and `.name` is the real name.

   The frontend already implements this join:
   `AdminReplayChatLogMessages.vue:157-167` does
   `log.value.players.find(x => x.id == playerId)?.name` on `message.fromPlayer`.

### Requirement: show both the battletag and the slot number

Moderators need to map a report ("Player 3 was toxic") onto a battletag, so the UI
must present **both identifiers together**. With `slotIndex` now persisted, render
`Player {slotIndex + 1} — {battleTag}` in the list row and in the chat log view.

### Constraint: never call the replay service in the list path

Replay-service calls incur real cost, so they must be strictly on-demand:

- The list endpoint proxies matchmaking only. No replay or chat call, ever — not
  to enrich names, not to check replay availability, not to resolve slots.
- Chat logs are fetched only when a moderator explicitly opens one for a single
  match. No prefetching, no fetch-on-hover, no batch resolution across the page.
- Availability is inferred from `floGameId` being non-null, which is free. Do not
  probe the replay service to find out whether a replay exists.

Persisting `slotIndex` upstream is what makes this satisfiable: without it, the
only source of slot numbers would be the chat log, meaning a paid call per row.

### Disclosure

Battletags are public information, not PII, so exposing them carries no special
handling burden — they can be logged and traced like any other field.

What this page does that warrants a gate is *linking* flo's anonymised
`Player N` back to an account, in modes that deliberately hide names in-game.
The sensitivity is the linkage, not the identifier.

Per decision, the Moderation permission gate is the only control — no audit
logging in v1. Enforce it server-side on every action; a client-side flag would
be insufficient, since on the masked paths the real value must never be sent to
an unauthorised client in the first place.

## Frontend design

A **new admin grid reusing the leaf components** — not an extension of
`MatchesGrid`. `MatchesGrid`'s rows are inline in its template (there is no
standalone row component), and its pagination is hard-bound to
`playerStore`/`matchStore` through an `isPlayerProfile` boolean, so a third
consumer cannot drive it without surgery. Its `unfinished` flag is also
overloaded — it hides the replay column, disables detail navigation, replaces the
duration with "onGoing" and zeroes the duration bar. Keeping the new page separate
leaves the player profile and public Matches page untouched.

### Files to create

| File | Purpose |
| --- | --- |
| `src/components/admin/AdminCancelledMatches.vue` | The page: `v-container.w3-container-width` > `v-card`, game-mode select, battletag search, paging, notes panel, table |
| `src/services/admin/CancelledMatchService.ts` | `AuthorizedClient` wrapper — **not** the deprecated `authorizedFetch` (`helpers/general.ts:31-42`) |
| `src/types/admin/CancelledMatch.ts` | DTOs mirroring the backend |
| `src/services/admin/CancelledMatchService.test.ts` | vitest + `node:assert`, fake `fetch` injected via constructor |

Follow the Jobs page as the template (`AdminJobs.vue`, `AdminJobService.ts`): lazy
module-level service singleton (`_service ??= new Service({ endpoint: API_URL })`,
because `@/config/env` reads `window` at module load), token from `useOauthStore()`
passed per call, `HttpError` status mapped to UI meaning at the boundary,
component-local refs rather than a Pinia store, and hardcoded English strings —
recent admin pages use no `$t` at all, and `src/locales/data.ts` is autogenerated
and overwritten by the build.

### Files to touch

| File | Change |
| --- | --- |
| `src/router/types.ts` | Add `CANCELLED_MATCHES = "Admin - Cancelled Matches"` to `EAdminRouteName` (the value is also the document title) |
| `src/router/index.ts` | Component import + one child entry in the `/admin` children array |
| `src/components/admin/AdminNavigation.vue` | Icon import + one child object in the **existing Moderation group** (lines 157-205) |
| `src/components/matches/PlayerMatchInfo.vue` | New `noWinner` prop |
| `src/components/matches/TeamMatchInfo.vue` | Pass `noWinner` through |
| `src/components/matches/DownloadReplayIcon.vue` | Optional `floGameId` prop |
| `src/components/admin/replays/AdminReplayChatLogMessages.vue` | Optional `floGameId` prop (currently `matchId` only, `:94`; calls the store at `:206`) |
| `src/store/admin/replayManagement/store.ts` | Add `loadChatLogByFloId(floGameId)` alongside `loadChatLog` (`:11`) |

The by-flo-id chat fetch belongs on the **new `CancelledMatchService`** using
`AuthorizedClient`, not beside `AdminService.getChatLog` (`:283`) — that method
uses the deprecated `authorizedFetch`, and extending it would perpetuate the
pattern the `AuthorizedClient` docstring explicitly steers new code away from. The
store action can call the new service directly.

The nav child's `component` value **must equal the route's `path` segment** — the
`/admin` landing redirect pushes `admin/${firstItem.component}`
(`AdminNavigation.vue:129-138`). Add it as a child of the existing Moderation group
rather than a new top-level group; a childless top-level entry breaks that
redirect.

### `noWinner` prop

`won` is the **only** genuine hard dependency. `PlayerMatchInfo.vue:135-143`
returns `"w3-won"`/`"w3-lost"` and `playerColorClass` applies it to three places:
the name link (`:21`) and **both** MMR-delta spans (`:27-28`, `:33-35`). When
`noWinner` is set, the computed returns `""`.

Do not reuse the existing `unfinishedMatch` flag for this — it also suppresses
spoiler logic, which is not wanted here.

Everything else degrades safely already, verified by reading the components:

- **MMR** — `PlayerMatchInfo.vue:150` guards `oldMmr != null`; `:157-163` requires
  both `oldMmr` and `currentMmr`; the template gates on `displayRating !== null`
  and `displayMmrChange !== 0`.
- **League/rank** — `:154-155`, `:203` optional-chain `ranking?.leagueOrder`,
  falling back to `views_rankings.unranked`.
- **Placement** — `TeamMatchInfo.vue:3,8` use `!isNil(...)` on `matchRanking`.
- **Heroes** — `HeroIconRow` accepts `Hero[] | null` and `v-for` over null renders
  nothing.
- `mmrGain` from the backend is dead on the frontend — the delta is always
  recomputed client-side.

Note that the proxied payload uses matchmaking's player shape
(`PlayerMMrChange`/`UnfinishedMatchPlayer`), not the read-model
`PlayerOverviewMatches` the grid components expect, so the page maps it to the
props those components take. Since `won` is simply absent from the source, there is
no falsy value to suppress.

### Slot number display

For anonymizing modes, render the slot number next to the battletag so a report
naming "Player 3" can be matched to an account — `Player 3 — <battletag>`, from
`slotIndex + 1`. When `slotIndex` is absent — a match that never reached flo game
creation — show the battletag alone; those matches have no chat log either.

Do **not** fetch chat logs to populate slot numbers in the list.

### Replay and chat buttons

`DownloadReplayIcon` currently hardcodes `${API_URL}api/replays/${gameId}` and
declares `gameId: String, required`. Add an optional `floGameId` prop that, when
present, targets the `by-flo-id` route instead. It already handles 429 and 404
distinctly, which is exactly what is needed here.

Render both buttons as disabled with an explanatory tooltip when `floGameId` is
null.

### Notes panel

The page carries a visible note explaining:

- Drawn and stalled matches are only marked cancelled after the 6-hour cleanup
  sweep, so a just-drawn match will not appear immediately.
- Replay and chat log are unavailable for matches cancelled before the flo game
  was created.
- No cancellation reason is recorded, so the list cannot say *why* a match was
  cancelled.

### In-page permission check

There is **no router guard and no route `meta` anywhere** in this frontend — the
whole `/admin` view is gated on `oauthStore.isAdmin` only, and any admin can
deep-link to `/admin/<any-page>`. Client gating today is purely the sidebar filter.
Add an in-page check following `AdminSmurfs.vue:220-221`:

```ts
const canModerate = computed(() =>
  oauthStore.permissions.includes(EPermission[EPermission.Moderation]));
```

and render a refusal state instead of the table. The backend 403 is still the real
enforcement; this stops the page rendering a broken shell.

## Testing

**matchmaking-service.** Cover the new repository query and route: `state`
filtering, `gameMode` filter, `battleTag` filter, paging maths
(`page * itemsPerPage - itemsPerPage`), `_updated_at` descending ordering, and
`requireAdmin` rejection. Add a case asserting `slotIndex` is populated on every
player after `setPlayersToSlots`, and that it equals the `CreateGameSlot[]` index.

**website-backend.** The established pattern for match code is NUnit repository
fixtures against a live Mongo via `IntegrationTestBase`, built with AutoFixture
through `TestDtoHelper`. There are **no** controller-level tests for
`MatchesController` to copy; controller test patterns exist elsewhere
(`AdminWarningsControllerTests.cs`) using hand-built controllers plus Moq — that is
the closer analogue here, since this controller is a proxy with no repository of
its own.

> **Caution:** `IntegrationTestBase` drops and recreates the real shared
> `W3Champions-Statistic-Service` database on a remote Mongo host in `[SetUp]`.

Cover:

- Controller returns 401 without Moderation, 200 with it.
- Query parameters are forwarded to `MatchmakingServiceClient` unmodified and
  `total` is surfaced unchanged.
- Deserialization of the matchmaking response into the existing `Match` DTO,
  including `slotIndex`, `_created_at`/`_updated_at`, and a null `floGameId`.
- The endpoint issues **no** replay-service call — assert against a mock
  `ReplayServiceClient` that it is never invoked.
- `ReplayRateLimitAttribute`: moderator token → hourly 50, daily unchanged,
  battleTag partition key; non-moderator and API-token paths unchanged.
- `GetChatLogs` maps an upstream non-success status to 404; `GenerateReplay` maps
  an upstream 404 to 404 rather than throwing.
- `GameModesHelper.IsFfaGameMode(GameMode.GM_FOOTMEN_FRENZY)` is true, the existing
  FFA modes still are, and `GM_LTW_FFA` is still false.

**Frontend.** vitest runs in `environment: "node"` with no vue plugin
(`vitest.config.ts:13-19`), so **`.vue` files cannot be unit tested** without adding
jsdom and `@vitejs/plugin-vue` first. Test the service layer only: assert URL,
method, query params and status mapping with an injected fake fetch.

Quality gates: `npm run lint` (`--max-warnings=0`), `npm run dprint`,
`npm run type-check`, `npm test`. Backend: `dotnet build`, `dotnet test`,
`dotnet format --verify-no-changes`.

## Deployment note

This design adds no read-model handler, so it does not depend on `START_HANDLERS`
and needs no backfill window. It does require the matchmaking-service change to be
deployed before the website-backend endpoint returns anything, and the new Mongo
index should be created before the endpoint is exercised at scale.

Per `CLAUDE.md`: read model handling is off by default locally, and connecting to
the wrong database can overwrite prod/test data.
