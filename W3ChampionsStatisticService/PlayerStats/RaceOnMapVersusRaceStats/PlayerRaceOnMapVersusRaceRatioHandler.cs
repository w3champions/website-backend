using System;
using System.Threading.Tasks;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.Matches;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Domain.GameModes;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

[Trace]
public class PlayerRaceOnMapVersusRaceRatioHandler(
    IPlayerStatsRepository playerRepository,
    IPatchRepository patchRepository
        ) : IReadModelHandler
{
    private readonly IPlayerStatsRepository _playerRepository = playerRepository;
    private readonly IPatchRepository _patchRepository = patchRepository;

    public async Task Update(MatchFinishedEvent nextEvent)
    {
        if (nextEvent.WasFakeEvent) return;

        var dataPlayers = nextEvent.match.players;
        if (dataPlayers.Count == 2)
        {
            var p1 = await _playerRepository.LoadMapAndRaceStat(dataPlayers[0].battleTag, nextEvent.match.season)
                        ?? PlayerRaceOnMapVersusRaceRatio.Create(dataPlayers[0].battleTag, nextEvent.match.season);
            var p2 = await _playerRepository.LoadMapAndRaceStat(dataPlayers[1].battleTag, nextEvent.match.season)
                        ?? PlayerRaceOnMapVersusRaceRatio.Create(dataPlayers[1].battleTag, nextEvent.match.season);

            DateTime start = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime date = start.AddMilliseconds(nextEvent.match.startTime);
            var patch = await _patchRepository.GetPatchVersionFromDate(date);

            if (GameModesHelper.IsMeleeGameMode(nextEvent.match.gameMode))
            {
                p1.AddMapWin(dataPlayers[0].race,
                    dataPlayers[1].race,
                    "Overall",
                    dataPlayers[0].won, patch);
                p1.AddMapWin(dataPlayers[0].race,
                    dataPlayers[1].race,
                    "Overall",
                    dataPlayers[0].won, "All");
                p2.AddMapWin(dataPlayers[1].race,
                    dataPlayers[0].race,
                    "Overall",
                    dataPlayers[1].won, patch);
                p2.AddMapWin(dataPlayers[1].race,
                    dataPlayers[0].race,
                    "Overall",
                    dataPlayers[1].won, "All");
            }

            p1.AddMapWin(dataPlayers[0].race,
                dataPlayers[1].race,
                new MapName(nextEvent.match.map).Name,
                dataPlayers[0].won, patch);
            p1.AddMapWin(dataPlayers[0].race,
                dataPlayers[1].race,
                new MapName(nextEvent.match.map).Name,
                dataPlayers[0].won, "All");
            p2.AddMapWin(dataPlayers[1].race,
                dataPlayers[0].race,
                new MapName(nextEvent.match.map).Name,
                dataPlayers[1].won, patch);
            p2.AddMapWin(dataPlayers[1].race,
                dataPlayers[0].race,
                new MapName(nextEvent.match.map).Name,
                dataPlayers[1].won, "All");

            await _playerRepository.UpsertMapAndRaceStat(p1);
            await _playerRepository.UpsertMapAndRaceStat(p2);
        }
    }
}
