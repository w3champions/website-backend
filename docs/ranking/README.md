# Progression ranking — read-model (downstream)

website-backend is **strictly downstream** for progression ranking: it ingests and retains the
published rank, it never computes it.

## Ingest path

The matchmaking service publishes a per-player progression-rank snapshot on the existing
`MatchFinishedEvent` (`match.players[].updatedProgression`), present only once a player has a
placed rank. The snapshot carries the published rank fields only:
`{ league, division, points, apexPoints }`.

- **DTO:** `UpdatedProgression` on `PlayerMMrChange` (`W3C.Domain/MatchmakingService/MatchEventDtos.cs`).
  All fields are `int?` — every value is integer-or-null by construction. Additive
  (`[BsonIgnoreExtraElements]`): events without the field deserialize it as `null`.
- **Read-model:** `PlayerProgression` (collection `PlayerProgression`) — one document per entity
  (a single player, or an arranged team), keyed via `BattleTagIdCombined`
  (`"{season}_{tags@gateway}_{gameMode}[_race]"`). Season is part of the `_id`, so prior seasons are
  retained automatically (no reset handler).
- **Handler:** `PlayerProgressionHandler : IMatchFinishedReadModelHandler` — mirrors
  `PlayerGameModeStatPerGatewayHandler` (arranged-team players grouped into one shared rank document;
  gameMode normalized to its AT variant; race in the key only for `GM_1v1` from season 2 on).
  Registered via `AddMatchFinishedReadModelService<PlayerProgressionHandler>()`.
- **Idempotency / retention:** a deterministic `_id` + `FindOneAndReplace(IsUpsert)` make replays a
  no-op overwrite; the read-model framework cursor (`HandlerVersions`) provides at-least-once delivery
  and skips stale-season events.

Read-model handlers are off by default locally (`IS_DEVELOPMENT`) — do not enable them against shared
data.

## Serving the rank to clients

The read-model is surfaced on the public rank DTOs as a nullable `progression` object carrying the
published fields only — `{ league, division, points, apexPoints }`:

- `Rank.Progression` (ladder, `Ladder/Rank.cs`) — populated in `RankQueryHandler` for `api/ladder/*`.
- `PlayerGameModeStatPerGateway.Progression` (profile, `PlayerProfiles/GameModeStats/...`) — populated
  in `GameModeStatQueryHandler` for `api/players/{tag}/game-mode-stats`.

Both stamp via `ProgressionViewLoader`, which batch-loads `PlayerProgression` by the shared composite id
and maps it to `PlayerProgressionView`. The field is a serve-time join (`[BsonIgnore]`, never stored) and
is `null` when the entity has no placed record for that season/mode. Additive — clients that ignore it
keep rendering the legacy RP fields.

## Apex leaderboard endpoint

`GET /api/ladder/apex?season=&gameMode=` (`LadderController`) serves the **apex** cohort — the combined
Grand Master and Master players for a season and game mode — as a single ordered leaderboard. It is
anonymous and gateway-agnostic (apex rank is global; there is no `gateWay` parameter).

- **Response:** `{ cutoffApexPoints, gmCount, players: [{ playersInfo, apexPoints, league, rankNumber }] }`.
  - `cutoffApexPoints` — the floating apex-points cutoff for the Grand Master tier (nullable; `null` before
    the cohort exists for that season/mode).
  - `gmCount` — how many of the listed players are Grand Master.
  - `players` — ordered Grand Master first, then Master, by apex points; `rankNumber` is the 1-based
    position in that order. `league` is the apex tier (`0` = Grand Master, `1` = Master). `playersInfo`
    carries the same per-player display data as the ladder rows (name, race, country, clan, picture).
  - A season/mode with no cohort yet returns `{ cutoffApexPoints: null, gmCount: 0, players: [] }`.
- **Read-model:** `ApexLeaderboard` (collection `ApexLeaderboard`, `Ladder/`) — one document per
  `(season, gameMode)`. Unlike the per-match progression read-model above, the apex cohort and its cutoff
  are recomputed upstream and **synced** into website-backend the same way the ladder itself is: the
  matchmaking service publishes an apex-standings snapshot, and an ingest handler keeps the read-model
  current. The endpoint serves this already-built document; it never computes the cohort.

## Progression league ladder endpoint

`GET /api/ladder/progression?season=&gameMode=&league=&division=&race=` (`LadderController`) serves a
single **non-apex** progression league/division page (Adept through Grass), read directly from the
`PlayerProgression` read-model. It is anonymous and gateway-agnostic.

- **Shape:** it returns the **same `Rank` array** as `GET /api/ladder/{leagueId}`, so clients reuse the
  existing ladder grid unchanged. Each row carries the `progression` object (`{ league, division, points,
  apexPoints }`) stamped from the same record and the usual `playersInfo`; `rankNumber` is the global
  points-descending position across the league/division page (offset by paging). The legacy RP fields are
  left at their defaults — clients render `progression`, not RP, in progression mode.
- **Ordering & paging:** rows are ordered by points descending. `skip`/`take` page the result; `take` is
  capped server-side at 500 so a caller cannot request an unbounded, fully enriched page.
- **Apex leagues:** leagues `0` (Grand Master) and `1` (Master) are apex and return an empty list here —
  use `GET /api/ladder/apex` for those.
- **Index:** a compound index on `PlayerProgression` over
  `(Season, GameMode, League, Division, Race, Points desc)` backs this league/division query so it does not
  scan the collection. It is created at startup via the standard index-initialization path (the repository
  implements `IRequiresIndexes`).

## Lifetime win-milestone read-model

A separate, **permanent** downstream read-model tracks each player's lifetime win-milestone progress — a
cosmetic "always something to climb" track that is independent of rank and never resets. It is fed from the
same event stream but keyed differently from the season-scoped progression rank above.

- **Read-model:** `ProgressionMilestone` (collection `ProgressionMilestone`,
  `PlayerProfiles/ProgressionStats/`) — one document per entity (a single player, or an arranged team) per
  game mode, and per race for `GM_1v1`. The `_id` is **season-less**
  (`"{tags@gateway}_{gameMode}[_race]"`), so wins accumulate across all seasons into one document
  (contrast `PlayerProgression`, whose `_id` includes the season).
- **What it stores:** `TotalWins` (monotonic, never decreases) and a compact rolling weekly activity
  window (`ActivityWeeks`, ~90 days) recording how many games the entity played each week. `LastPlayedAt`
  is retained for a future display path.
- **Source of truth:** the per-player `won` flag on `MatchFinishedEvent` (`PlayerMMrChange.won`) — counted
  for every match, independent of whether a progression rank was recorded. `TotalWins` increments on a win;
  the activity window records every game (won or lost).
- **Handler:** `ProgressionMilestoneHandler : IMatchFinishedReadModelHandler` — groups arranged-team players
  by `atTeamId` into one shared team document and processes solo players individually (mirrors
  `PlayerProgressionHandler`); skips fake events. Registered via
  `AddMatchFinishedReadModelService<ProgressionMilestoneHandler>()`.
- **Next-milestone target:** `MilestoneTargetCalculator` is a pure function of `TotalWins` and the entity's
  own recent activity. The target is the next round-number of wins on a curve whose step coarsens as totals
  grow; a returning or low-activity player is given a *nearer* milestone (never a farther one). It is
  computed **on read** — nothing is stored for it and there is no scheduled job.
- **Idempotency:** `TotalWins` is an additive counter, so — like the other counting read-models
  (`PlayerOverallStats`, `GamesPerDay`) — it relies on the read-model cursor (`HandlerVersions`) for
  at-least-once delivery rather than a per-document guard. A deliberate cursor rewind/backfill would
  re-count; this is accepted, consistent with those existing handlers.
- **Serving to clients (owner-private endpoint):** the milestone is **not** placed on the public profile
  DTO. It is served only to the authenticated user, for their own milestones, via a dedicated endpoint
  `GET /api/players/my-milestones` (`PlayersController`, `[InjectActingPlayerAuthCode]`). The caller's
  battleTag is taken from the JWT — never from the route — so a caller can only ever read their own data.
  The response is a flat JSON list of `MilestoneDto` `{ gameMode, gateWay, race, currentWins,
  previousTarget, nextTarget }` (`gameMode`/`gateWay`/`race` are the numeric enum ids; `race` is `null` for
  non-race-split modes). `currentWins`/`previousTarget`/`nextTarget` are the raw-wins progress bar to the
  next round number (`previousTarget` is the current band's lower bound, so a client renders an in-band fill
  that resets after each milestone). `MilestoneQueryHandler.LoadForPlayer` calls
  `IProgressionMilestoneRepository.LoadMilestonesForPlayer(battleTag)` — which returns every milestone doc
  whose `PlayerIds` includes the caller, i.e. their **solo** docs and any **arranged-team** doc they are a
  member of — and maps each through `MilestoneTargetCalculator`. The endpoint is consumed by launcher-e
  only; nothing milestone-related appears on the anonymous profile or ladder DTOs.

## Prestige store (peak rank)

`ProgressionPrestige` is a permanent, per-player read-model (one document per battleTag) recording the
**highest progression rank ever reached** in each individual-rank game mode (per race for race-split
modes). Each mode entry keeps an all-time peak, the peak reached in each season, and a reserved slot for
future cosmetic badges. The peak only ever rises, so it is retained across the seasonal reset (the
current-season rank lives in the season-keyed progression read-model; the peak here is never cleared).
It is built from the match-finished event stream and is ingest-only — there is no read API yet.

- **Read-model:** `ProgressionPrestige` (collection `ProgressionPrestige`,
  `PlayerProfiles/ProgressionStats/`) — one document per player (battleTag). Each document holds a list
  of `PrestigePeakEntry` records, one per `(gameMode, race)` combination. Race is populated only for
  race-split game modes (e.g. `GM_1v1`); it is `null` for non-race-split modes.
- **What it stores:** `AllTimePeak` — the single highest rank the player has ever held in that
  mode/race — and `SeasonPeaks`, a per-season list of the peak rank reached within each individual
  season. The all-time peak is always equal to the best entry across all season peaks (invariant
  maintained on every write). A `Badges` list is reserved for future cosmetic awards; it is always
  empty in the current stage.
- **Scope:** only **non-arranged-team** placements are recorded. Arranged-team rank is attributed to
  the team as a whole, not to individual players, so those events are skipped.
- **Handler:** `ProgressionPrestigeHandler : IMatchFinishedReadModelHandler` — for each placed,
  non-arranged-team player it reads (or creates) the player's prestige document, applies the peak
  candidate, and upserts the result. The update is a monotonic `max`; replaying the same event is a
  no-op. Skips fake events. Registered via
  `AddMatchFinishedReadModelService<ProgressionPrestigeHandler>()`.
- **Idempotency:** the peak is updated only when the incoming rank strictly exceeds the stored peak,
  so replays and out-of-order deliveries are safe. The read-model cursor (`HandlerVersions`) provides
  at-least-once delivery.
