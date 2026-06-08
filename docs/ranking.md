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
