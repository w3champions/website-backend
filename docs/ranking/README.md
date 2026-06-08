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
