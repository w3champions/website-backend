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
