# Progression ranking — website-backend

> Read-model ingest + serving of the progression rank is documented in [ranking/README.md](ranking/README.md).

website-backend serves the ranking data the website and launcher render; it does not compute rank. A new
progression ranking system runs alongside the legacy "RP" ladder. Each game mode uses exactly one of the
two, selected per mode by a season flag sourced from matchmaking-service.

## `ActiveGameMode.ProgressionStartSeason`

`api/ladder/active-modes` (`Ladder/LadderController.cs`) returns the active game modes. Each carries
`ProgressionStartSeason` (`int?`) on the `ActiveGameMode` DTO
(`W3C.Domain/MatchmakingService/MatchmakingServiceClient.cs`):

- `null` / absent — the mode uses the legacy RP ladder.
- a number `N` — the mode uses the new progression system from season `N` onward.

The endpoint forwards the raw season value unchanged; the client decides which system to render for the
season it is viewing (a past season keeps whatever it used, so a current-season-resolved label would be
wrong for history). The field is additive — clients that ignore it keep rendering RP.

## File reference

| Concern | Path |
|---|---|
| `ActiveGameMode` DTO | `W3C.Domain/MatchmakingService/MatchmakingServiceClient.cs` |
| `active-modes` endpoint | `W3ChampionsStatisticService/Ladder/LadderController.cs` |
| Contract test | `WC3ChampionsStatisticService.UnitTests/MatchmakingService/ActiveGameModeTests.cs` |

## Progression bracket metrics

The existing `/metrics` endpoint (scraped by the `websitebackend` Prometheus job) exposes two gauges
tracking the current season's ladder population. The current season is determined as the highest season
present in the `PlayerProgression` collection.

| Metric | Labels | Description |
|---|---|---|
| `progression_bracket_count` | `gameMode`, `league`, `division` | Ranked entries in each league/division bracket |
| `progression_ranked_total` | `gameMode` | Total ranked entries across all brackets, per game mode |

The `league` label uses the `ProgressionLeague` enum name (e.g. `Gold`, `Silver`, `Bronze`). The
`division` label is an empty string for leagues that have no divisions (GrandMaster, Master).

Series that are no longer present in the latest data (e.g. after a season rollover) are removed on the
next refresh so they do not linger as stale gauges.

**Enabling:** the background service that publishes these gauges is off by default. Set
`PROGRESSION_METRICS_ENABLED=true` to enable it. The service refreshes every 15 minutes.
