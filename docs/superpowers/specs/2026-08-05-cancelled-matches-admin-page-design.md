# Cancelled Matches Admin Page — Design

**Date:** 2026-08-05
**Status:** Approved design, ready for implementation planning
**Repos affected:** `w3c-backend`, `w3c-website`
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
(`matches.manager.ts:296-301`), which is the only path that emits
`MatchCanceledEvent` (`:316-322`, ladder-only).

**Consequence: a drawn match does not appear for roughly 6 hours.** Accepted for
v1; the page carries an explanatory note (see Frontend). Revisiting this by
plumbing the W3GS `LeaveDraw` reason (`flo/crates/w3gs/src/protocol/constants.rs:188`,
already decoded by flo's observer edge) through to matchmaking is deferred.

### No cancellation reason is recorded anywhere

`MatchesRepo.cancelMatch` (`matches.repo.ts:79-84`) sets `state = CANCELED` and
nothing else — no reason, no `endTime`, no `lengthSeconds`. A rich
`EGameCreationResult` enum (23 values) exists but is only written to match logs
and metrics, never onto the `Match` entity, so it cannot ride along on the
event. The page therefore cannot show *why* a match was cancelled.

### There are four `EMatchState` values and no draw state

`INIT`, `STARTED`, `FINISHED`, `CANCELED` (`match.ts:44-49`). A draw is
indistinguishable from "nobody reported a win".

## Scope

**In scope:** ladder matches cancelled by the timeout sweeps — `INIT` older than
2 minutes, and `STARTED` older than 6 hours (the bucket that swallows draws).
These are the only cancellations that reach this backend today.

**Out of scope, to be raised at PR review:**

1. **Game-creation-failure cancellations emit no event at all.**
   `decideMatchOutcomeDuringFailedGameCreation` (`matches.manager.ts:148-171`)
   calls `cancelMatch` directly and, unlike `:319`, never calls
   `statsHook.pushMatchCanceled`. This silently drops flo create-game errors,
   join bugs, disbanded teams, leavers, and every per-state timeout. This looks
   like a genuine inconsistency rather than a design decision, and the page will
   not show any of these matches until it is fixed.
2. **`LeaveDraw` is not treated as a first-class outcome** — see above. Would
   remove the ~6h delay.
3. **Tournament and custom matches never reach the stats hook** (the
   `isLadderMatch` gate at `matches.manager.ts:318`). Tournament code also uses
   `CANCELED` as an outcome marker while still assigning a winner
   (`tournament.manager.ts:306-365`), so including them needs semantics
   untangled first.
4. **`GM_FOOTMEN_FRENZY` mismatch.** It sets `isAnonymous = true` upstream but is
   absent from `GameModesHelper.FfaGameModes` in the backend. (`GM_LTW_FFA` being
   absent is correct — `gm-ltw-ffa.ts:30` sets `isAnonymous = false`.)

## Backend design

### 1. New `CanceledMatch` read model

Nothing queryable exists today. The only trace of a cancellation is the raw
`MatchCanceledEvent` document, inserted directly by matchmaking's stats hook
(`stats-hook.ts:106-111`, `insertOne` with no top-level `_id`, so no dedup). The
backend never inserts into, indexes, or deletes that collection; its sole read is
`MatchEventRepository.Load<T>(lastObjectId, pageSize)`, a forward-only
ascending-`_id` cursor with no filters, no descending sort and no count. Only two
handlers consume the event, and neither records anything reusable:
`OngoingRemovalMatchCanceledHandler` deletes the ongoing-match row, and
`LagReportMatchCanceledHandler` enriches an already-existing lag report.
`Matchup` has no state field and its only factory is `Create(MatchFinishedEvent)`.

Add `CanceledMatchReadModelHandler : IMatchCanceledReadModelHandler`, registered
in `Program.cs` via the existing `AddMatchCanceledReadModelService`.

**Backfill is free.** A new handler gets its own `HandlerVersions` row, and
`VersionRepository.GetLastVersion` defaults to `LastVersion = ObjectId.Empty`,
`Season = 0`. `UpdateSeasonIfNeeded` raises the stored season monotonically as
the handler walks `_id` order, so a fresh handler replays the entire historical
`MatchCanceledEvent` backlog on first deploy.

> **Verify before relying on full history.** No TTL index and no delete path
> exists for that collection anywhere in either repo (the only TTLs are
> `LagReport` at 90 days and `PlayerMatchTelemetry`; the only event delete is
> `DeleteStartedEvent`). That is a claim about *code* — a manually-created TTL
> index or an ops-side prune job would not be visible in the repos. Check the
> live cluster before promising complete history.

#### Entity

| Field | Source | Notes |
| --- | --- | --- |
| `Id` | source event `ObjectId` | |
| `MatchId` | `match._id` | 10-char nanoid; unique key |
| `FloGameId` | `match.floGameId` | `int?` — genuinely absent for pre-flo cancels |
| `GameMode`, `GameName` | `match` | |
| `Map`, `MapId`, `MapName` | `match` | |
| `Gateway`, `Season`, `ServerProvider`, `FloNode` | `match` | |
| `StartTime` | `match.startTime` | unix ms |
| `CanceledAt` | **event `ObjectId` timestamp** | see below |
| `Players` | `match.players` | dedicated DTO, see below |

**`CanceledAt` must derive from the event ObjectId's timestamp, not `endTime`.**
`endTime` is never set upstream on a cancellation and the C# `Match` DTO coerces
the null to `0` (`MatchEventDtos.cs:124-136`), so it cannot be used as a sort
key. `StartTime` is a viable alternative but is further from "when did this get
cancelled".

**Use a dedicated `CanceledMatchPlayer` DTO — do not reuse `PlayerOverviewMatches`.**
Fields: `BattleTag`, `Name`, `Race`, `Team`, `OldMmr`, `Ranking`, `Country`,
`InviteName`. Deliberately **no** `Won`, `CurrentMmr`, `MmrGain` or
`MatchRanking`. Reusing the existing DTO would be actively harmful:
`PlayerOverviewMatches.Won` is a non-nullable `bool` and `Team.Won` is a computed
getter, so `won: false` would always be on the wire, and
`PlayerMatchInfo.vue:135-143` would paint every player as a loser. Omitting the
field is cleaner than shipping a falsy value and suppressing it downstream.

#### Indexes and dedup

Upsert by `MatchId` with a unique index — upstream uses `insertOne` with no
dedup, and the producer's 300-entry queue can re-send on failure, so the same
match can legitimately arrive twice.

- unique `MatchId`
- `{ GameMode: 1, CanceledAt: -1 }` — the by-mode listing
- `{ CanceledAt: -1 }` — the all-modes listing
- `{ "Players.BattleTag": 1 }` — battletag filter

Follow the existing `IRequiresIndexes` pattern (see `MatchRepository.EnsureIndexesAsync`).

### 2. Remove healed matches

`healMatches` runs every 12 hours and retroactively converts a `CANCELED` ladder
match from the last 2 days into `FINISHED` when a matching winner-bearing result
log turns up (`matches.manager.ts:326-357`). So a match can emit
`MatchCanceledEvent` first and `MatchFinishedEvent` later.

Add `CanceledMatchRemovalOnFinishHandler : IMatchFinishedReadModelHandler` that
deletes the `CanceledMatch` row by `MatchId`. This mirrors the existing
`OngoingRemovalMatchCanceledHandler` pattern exactly. Without it, healed matches
linger as phantom rows on the admin page.

(True draws are never healed — a heal still requires a winner — so this only
affects the genuine cancel-then-finish race.)

### 3. New endpoint

New `AdminCanceledMatchesController`. Every action carries
`[BearerHasPermissionFilter(Permission = EPermission.Moderation)]` — the
attribute is `AttributeTargets.Method`, so it cannot be applied at controller
level and must be repeated per action.

```
GET api/admin/canceled-matches?gameMode=&battleTag=&offset=&pageSize=
    -> { matches, count }
```

- Envelope `{ matches, count }` matches the match-domain convention the frontend
  already consumes (`MatchesController` uses it for `/api/matches` and
  `/api/matches/search`); the newer `{ items, total }` shape used by lag reports
  is the other option, but consistency with the match endpoints wins here.
- `gameMode` accepts `GameMode.Undefined` (0) to mean **all modes**. This needs
  an explicit bypass branch — the existing `MatchRepository.GetLoadFilter` does a
  bare `builder.Eq(m => m.GameMode, gameMode)` with no Undefined handling.
- `battleTag` is an optional filter; moderation workflows usually start from a
  player.
- Clamp `pageSize` to 100, consistent with `MatchesController`.
- Sort `CanceledAt` descending.

Do **not** apply `PlayersObfuscator`. It exists for ongoing FFA matches and for
MMR/rank-deviation masking on public endpoints; this is a Moderation-gated admin
surface that needs real values.

### 4. Replay and chat log reachability

Both the replay download and the chat log bottom out in the **same single
identifier — the flo game id (int)**. There is no second id to be missing:
`ReplaysController` `{gameId}` and `{gameId}/chats` both call
`MatchRepository.GetFloIdFromId` and pass the resulting int to
`ReplayServiceClient`, whose two methods format that int into
`/generate/{id}` and `/chats/{id}`.

`GetFloIdFromId` reads the **`Matchup` collection only** (`MatchRepository.cs:275-280`).
A cancelled match has no `Matchup` row, so the `{gameId}` routes cannot ever
serve one. Worse, `new ObjectId($"{gameId}")` at `:277` is unguarded, so passing
the matchmaking nanoid yields a 500 rather than a clean 404.

**Therefore: no new replay or chat endpoints. The list payload exposes
`floGameId`, and the frontend uses the existing `by-flo-id` routes**, which do no
DB lookup at all and only reject `0`.

`FloGameId` is nullable and sometimes genuinely absent: it is assigned only after
flo's `createGame` returns (`flo-game-creation.flow.ts:172-174`) and persisted
only in `startMatch` (`match-maker.ts:149-152`). The 2-minute `INIT` sweep
cancels matches that never got a flo game — `LagReportMatchCanceledHandler.cs:21-24`
already early-returns on this. **For those matches no replay and no chat log can
ever exist**; the UI must render the buttons as unavailable rather than failing.

Three supporting changes:

#### 4a. Raise the hourly replay rate limit for authenticated moderators

Cancelled matches always land in the **strict** bucket, because
`ReplayRateLimitAttribute.CheckMatchAge` resolves age via
`LoadFinishedMatchDetailsBy*`, which returns null for a cancelled match →
`isRecent == null` → strict (10/hour, 50/day). Ten downloads an hour is thin for
a moderator working through a list.

Change `ReplayRateLimitAttribute`:

- Add `ModeratorHourlyLimit { get; set; } = 50`.
- After `rateLimitService.DetermineRateLimitContext(...)` returns, if the request
  carries a valid bearer token whose permissions include `EPermission.Moderation`
  **and** `!ctx.HasValidApiToken`, then set `ctx.HourlyLimit = ModeratorHourlyLimit`,
  `ctx.PolicyName = "replay-moderator"`, and
  `ctx.PartitionKey = $"moderator:{battleTag}:{Scope}"`.
- **Leave `DailyLimit` untouched** — it stays at whatever strict/relaxed
  resolved (50 strict, 100 relaxed).

Resolve the auth service from DI: `IW3CAuthenticationService` is registered at
`Program.cs:178` as an intercepted transient, so use
`RequestServices.GetRequiredService<IW3CAuthenticationService>()` rather than
`BearerHasPermissionFilter`'s `new W3CAuthenticationService()`.

Partitioning by battleTag rather than IP is deliberate: it keeps one moderator
from consuming another's budget behind a shared IP, and matches the existing key
conventions (`ip:{ip}:{scope}`, `token:{id}:{scope}`).

Note that with hourly at 50 and strict daily at 50, the daily cap becomes the
binding constraint — a moderator can spend the whole day's allowance in one hour.
That is the intended trade-off: bursty review sessions are the actual usage
pattern, and the daily ceiling still holds.

Unauthenticated and non-moderator behaviour is unchanged.

#### 4b. Make a missing chat log return 404

`ReplayServiceClient.GetChatLogs` (`ReplayServiceClient.cs:29-33`) uses
`GetAsync` + `ReadAsStringAsync` with **no status check**, so an upstream 404
deserializes to null and surfaces as HTTP 200 with a null body. This page will
hit missing chat logs routinely (pre-flo cancels, unarchived replays), so it
should return a clean 404.

(`GenerateReplay` does not have this problem — `GetStreamAsync` applies
`EnsureSuccessStatusCode` internally. It has the opposite one: an upstream 404
becomes an unhandled exception → 500. Worth tidying while in there.)

#### 4c. Never return `playerScores: []`

If any payload feeding `MatchDetail.vue` carries `playerScores: []` instead of
`null`, the page silently flips into "complete game" mode: `isCompleteGame`
(`MatchDetail.vue:303`) is typed `PlayerScore[]`, not boolean, and an empty array
is truthy. That suppresses the `incompletedata` message, renders an all-zeros
score screen, and unlocks the unguarded FFA dereferences at `:335` and `:487-499`.
`null` is the correct value and is what the existing code already produces.

## FFA de-anonymization

The requirement is to reconcile who is who in modes that hide names in-game.
**This works with no new mapping table and no upstream change.**

### Where "Player N" comes from

Not this backend. Flo masks names as exactly `format!("Player {}", i + 1)` where
`i` is the **slot index**, at `observer-edge/src/game/snapshot.rs:187-191` and
`observer-edge/src/game/mod.rs:173` among others. It is driven by
`game.mask_player_names`, which matchmaking sets from
`gameMode?.isAnonymous || match.hideCustomGamePlayerNames`
(`flo-game-creation.flow.ts:160`). `isAnonymous` is true for `GM_FFA`,
`GM_SC_FFA_4`, `GM_SC_OZ` and `GM_FOOTMEN_FRENZY`.

The backend's own FFA obfuscation is a separate mechanism that produces `"*"`,
not `Player N`, applies at API-response time only, and only to **ongoing**
matches (`PlayersObfuscator.cs:18-19`, called from `MatchesController.cs:190,206`).

### Both identity sources are already unmasked

1. **`MatchCanceledEvent.match.players[].battleTag` is real.** Matchmaking pushes
   the live `Match` object verbatim into the stats DB; it anonymizes nothing in
   its event payloads. So the new read model captures real battletags directly,
   and the list shows real names with no extra work.

2. **The chat log already resolves slot → real name.** replay-service's chats
   handler calls flo's gRPC `get_game`, and `Game::pack()`
   (`controller/src/game/types.rs:45-68`) packs `slots` verbatim **without
   masking** — masking lives only in the observer-edge, client and player-sender
   paths. The handler then builds `PlayerInfo { id: idx + 1, name: player.name }`
   over the same slot enumeration (`w3c-replay/binaries/replay-service/src/handlers/chats.rs:139-151`).

   Because both use `slots.iter().enumerate()`, **`chatLog.players[].id` is
   exactly the N in "Player N"**, and `.name` is the real name. "Player 3 was
   toxic" resolves by looking up `players[].id == 3`.

   The frontend already implements this join:
   `AdminReplayChatLogMessages.vue:157-167` does
   `log.value.players.find(x => x.id == playerId)?.name` on `message.fromPlayer`.

So the entire FFA requirement reduces to **making the chat log reachable by flo
game id**, which 4a–4b above already cover.

### The one thing that is not recoverable

If no replay/chat log exists (pre-flo-creation cancels), there is no way to map a
slot number to a battletag. Matchmaking computes slot assignment transiently in
`player-slots.helper.ts:14-29` for the flo `CreateGame` request and discards it;
it is never written to the `Match` object. `LagReport.ServerSidePing[].PlayerName`
is no help — it stores flo's *masked* `Player N` strings, and the backend has no
flo-player-id → battletag mapping anywhere. Fixing this properly would mean
persisting the slot map on the `Match` object upstream. Not needed for v1, since
the participant set is always available from the list.

### PII

Per decision, the Moderation permission gate is the only control — no audit
logging in v1. Worth stating plainly in the PR: this page shows real battletags
for game modes that deliberately hide names in-game, so the Moderation gate is
the sole thing standing between an admin without Moderation and that data.
Enforce it server-side on every action; a client-side flag would be
insufficient.

## Frontend design

A **new admin grid reusing the leaf components** — not an extension of
`MatchesGrid`. `MatchesGrid`'s rows are inline in its template (there is no
standalone row component), and its pagination is hard-bound to
`playerStore`/`matchStore` through an `isPlayerProfile` boolean, so a third
consumer cannot drive it without surgery. Its `unfinished` flag is also
overloaded — it hides the replay column, disables detail navigation, replaces the
duration with "onGoing" and zeroes the duration bar. Keeping the new page
separate leaves the player profile and public Matches page untouched.

### Files to create

| File | Purpose |
| --- | --- |
| `src/components/admin/AdminCancelledMatches.vue` | The page: `v-container.w3-container-width` > `v-card`, game-mode select, battletag search, paging, notes panel, table |
| `src/services/admin/CancelledMatchService.ts` | `AuthorizedClient` wrapper — **not** the deprecated `authorizedFetch` (`helpers/general.ts:31-42`) |
| `src/types/admin/CancelledMatch.ts` | DTOs mirroring the backend |
| `src/services/admin/CancelledMatchService.test.ts` | vitest + `node:assert`, fake `fetch` injected via constructor |

Follow the Jobs page as the template (`AdminJobs.vue`, `AdminJobService.ts`):
lazy module-level service singleton (`_service ??= new Service({ endpoint: API_URL })`,
because `@/config/env` reads `window` at module load), token from
`useOauthStore()` passed per call, `HttpError` status mapped to UI meaning at the
boundary, component-local refs rather than a Pinia store, and hardcoded English
strings — recent admin pages use no `$t` at all, and `src/locales/data.ts` is
autogenerated and overwritten by the build, so adding locale keys is awkward.

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
pattern the `AuthorizedClient` docstring explicitly steers new code away from.
The store action can call the new service directly.

The nav child's `component` value **must equal the route's `path` segment** — the
`/admin` landing redirect pushes `admin/${firstItem.component}`
(`AdminNavigation.vue:129-138`). Add it as a child of the existing Moderation
group rather than a new top-level group; a childless top-level entry breaks that
redirect.

### `noWinner` prop

`won` is the **only** genuine hard dependency. `PlayerMatchInfo.vue:135-143`
returns `"w3-won"`/`"w3-lost"` and `playerColorClass` applies it to three places:
the name link (`:21`) and **both** MMR-delta spans (`:27-28`, `:33-35`). When
`noWinner` is set, the computed returns `""`.

Do not reuse the existing `unfinishedMatch` flag for this — it also suppresses
spoiler logic, which is not wanted here.

Everything else degrades safely already, verified by reading the components:

- **MMR** — `PlayerMatchInfo.vue:150` guards `oldMmr != null`; `:157-163`
  requires both `oldMmr` and `currentMmr`; the template gates on
  `displayRating !== null` and `displayMmrChange !== 0`.
- **League/rank** — `:154-155`, `:203` optional-chain `ranking?.leagueOrder`,
  falling back to `views_rankings.unranked`.
- **Placement** — `TeamMatchInfo.vue:3,8` use `!isNil(...)` on `matchRanking`.
- **Heroes** — `HeroIconRow` accepts `Hero[] | null` and `v-for` over null
  renders nothing.
- `mmrGain` from the backend is dead on the frontend — the delta is always
  recomputed client-side. Another reason to omit it from the DTO.

### Replay and chat buttons

`DownloadReplayIcon` currently hardcodes `${API_URL}api/replays/${gameId}` and
declares `gameId: String, required`. Add an optional `floGameId` prop that, when
present, targets the `by-flo-id` route instead. It already handles 429 and 404
distinctly, which is exactly what is needed here.

Render both buttons as disabled with an explanatory tooltip when `floGameId` is
null — those matches were cancelled before flo created the game and can never
have a replay or chat log.

### Notes panel

Per decision, the page carries a visible note explaining:

- Drawn and stalled matches appear only after the 6-hour cleanup sweep.
- Matches cancelled during game creation (flo errors, join bugs, leavers) are
  **not** listed, because they emit no event today.
- Tournament and custom matches are not listed.
- Replay and chat log are unavailable for matches cancelled before the flo game
  was created.

### In-page permission check

There is **no router guard and no route `meta` anywhere** in this frontend — the
whole `/admin` view is gated on `oauthStore.isAdmin` only, and any admin can
deep-link to `/admin/<any-page>`. Client gating today is purely the sidebar
filter. Add an in-page check following `AdminSmurfs.vue:220-221`:

```ts
const canModerate = computed(() =>
  oauthStore.permissions.includes(EPermission[EPermission.Moderation]));
```

and render a refusal state instead of the table. The backend 403 is still the
real enforcement; this stops the page rendering a broken shell.

## Testing

**Backend.** The established pattern for match code is NUnit repository fixtures
against a live Mongo via `IntegrationTestBase`, built with AutoFixture through
`TestDtoHelper` (see `WC3ChampionsStatisticService.UnitTests/Matchups/`). There
are **no** controller-level tests for `MatchesController` to copy; controller test
patterns exist elsewhere (`AdminWarningsControllerTests.cs`) using hand-built
controllers plus Moq.

> **Caution:** `IntegrationTestBase` drops and recreates the real shared
> `W3Champions-Statistic-Service` database on a remote Mongo host in `[SetUp]`.

Cover:

- Handler writes a `CanceledMatch` from a `MatchCanceledEvent`
  (`TestDtoHelper` already has cancelled-event helpers).
- Handler is idempotent — same match twice yields one row.
- `FloGameId == null` is persisted without error.
- Repository: gameMode filter, `Undefined` = all modes, battleTag filter, paging,
  `CanceledAt` descending.
- `CanceledMatchRemovalOnFinishHandler` deletes the row on a matching finish.
- `ReplayRateLimitAttribute`: moderator token → hourly 50, daily unchanged,
  battleTag partition key; non-moderator and API-token paths unchanged.
- `GetChatLogs` maps an upstream non-success status to 404.

**Frontend.** vitest runs in `environment: "node"` with no vue plugin
(`vitest.config.ts:13-19`), so **`.vue` files cannot be unit tested** without
adding jsdom and `@vitejs/plugin-vue` first. Test the service layer only:
assert URL, method, query params and status mapping with an injected fake fetch.

Quality gates: `npm run lint` (`--max-warnings=0`), `npm run dprint`,
`npm run type-check`, `npm test`. Backend: `dotnet build`, `dotnet test`,
`dotnet format --verify-no-changes`.

## Deployment note

Read-model handlers are gated on `START_HANDLERS` and the comparison is
**case-sensitive exact equality** with `"true"` (`Program.cs:267`) — `"True"`,
`"TRUE"` and `"1"` all silently disable every handler. The new handler will not
backfill until that is set correctly in the target environment.

Per `CLAUDE.md`: read model handling is off by default locally, and connecting to
the wrong database can overwrite prod/test data.

## Correction to `CLAUDE.md`

`CLAUDE.md` names `../website` as the Vue 3 frontend. In this checkout
`../website` is the **Caddy web server's site** (remote:
`git@github.com:caddyserver/website.git`). The actual frontend is
`../w3c-website`. Worth fixing in the same PR.
