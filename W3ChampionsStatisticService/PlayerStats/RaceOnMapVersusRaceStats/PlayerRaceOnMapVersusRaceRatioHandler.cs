using System;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats
{
    public class PlayerRaceOnMapVersusRaceRatioHandler : IReadModelHandler
    {
        private readonly IPlayerStatsRepository _playerRepository;
        private readonly IPatchRepository _patchRepository;

        public PlayerRaceOnMapVersusRaceRatioHandler(
            IPlayerStatsRepository playerRepository,
            IPatchRepository patchRepository
            )
        {
            _playerRepository = playerRepository;
            _patchRepository = patchRepository;
        }

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

                DateTime start = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                DateTime date = start.AddMilliseconds(nextEvent.match.startTime);
                var patch = await _patchRepository.GetPatchVersionFromDate(date);

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
                p1.AddMapWin(dataPlayers[0].race,
                    dataPlayers[1].race,
                    nextEvent.match.mapName,
                    dataPlayers[0].won, patch);
                p1.AddMapWin(dataPlayers[0].race,
                dataPlayers[1].race,
                nextEvent.match.mapName,
                dataPlayers[0].won, "All");
                p2.AddMapWin(dataPlayers[1].race,
                    dataPlayers[0].race,
                    nextEvent.match.mapName,
                    dataPlayers[1].won, patch);
                p2.AddMapWin(dataPlayers[1].race,
                    dataPlayers[0].race,
                    nextEvent.match.mapName,
                    dataPlayers[1].won, "All");
                await _playerRepository.UpsertMapAndRaceStat(p1);
                await _playerRepository.UpsertMapAndRaceStat(p2);
            }
        }
    }
}