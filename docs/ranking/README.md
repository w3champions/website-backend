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
- **Serving to clients (profile only):** the milestone is surfaced on the profile DTO as a nullable
  `PlayerGameModeStatPerGateway.Milestone` object carrying only the three player-facing numbers
  `{ currentWins, previousTarget, nextTarget }` — the raw-wins progress bar to the next round number
  (`previousTarget` is the current band's lower bound, so a client renders an in-band fill that resets
  after each milestone). It is populated in `GameModeStatQueryHandler` (`api/players/{tag}/game-mode-stats`)
  via `MilestoneViewLoader`, a serve-time join (`[BsonIgnore]`, never stored) that batch-loads the
  milestone docs and maps each through `MilestoneTargetCalculator` into a `PlayerMilestoneView`. Because the
  milestone `_id` is season-less while the stat `_id` is season-keyed, the loader reconstructs the
  milestone id from the stat's components rather than reusing the stat id. `null` when the entity has no
  milestone doc. It is **profile-only** (a per-player progress bar, not a ladder column — the ladder DTO is
  not extended). A pre-`RaceSplitStartSeason` 1v1 stat row is race-collapsed (`Race == null`) and so yields
  no milestone, since one collapsed row cannot be attributed to one of several per-race lifetime milestones.

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
