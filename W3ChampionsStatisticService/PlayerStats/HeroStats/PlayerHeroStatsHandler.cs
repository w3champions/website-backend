using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerStats.HeroStats
{
    public class PlayerHeroStatsHandler : IReadModelHandler
    {
        private readonly IPlayerStatsRepository _playerRepository;

        public PlayerHeroStatsHandler(
            IPlayerStatsRepository playerRepository
            )
        {
            _playerRepository = playerRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            if (nextEvent == null || nextEvent.match == null || nextEvent.result == null)
            {
                return;
            }

            var dataPlayers = nextEvent.match.players;
            if (dataPlayers?.Count == 2 && nextEvent.result.players?.Count == 2)
            {
                var eventPlayer1 = dataPlayers[0];
                var eventPlayer2 = dataPlayers[1];

                var blizzardInfoPlayer1 = nextEvent.result.players[0];
                var blizzardInfoPlayer2 = nextEvent.result.players[1];

                var p1 = await _playerRepository.LoadHeroStat(eventPlayer1.battleTag, nextEvent.match.season)
                         ?? PlayerHeroStats.Create(eventPlayer1.battleTag, nextEvent.match.season);
                var p2 = await _playerRepository.LoadHeroStat(eventPlayer2.battleTag, nextEvent.match.season)
                         ?? PlayerHeroStats.Create(eventPlayer2.battleTag, nextEvent.match.season);

                p1.AddMapWin(blizzardInfoPlayer1, eventPlayer1.race,
                    eventPlayer2.race,
                    "Overall",
                    dataPlayers[0].won);
                p2.AddMapWin(blizzardInfoPlayer2, eventPlayer2.race,
                    eventPlayer1.race,
                    "Overall",
                    dataPlayers[1].won);

                p1.AddMapWin(blizzardInfoPlayer1, eventPlayer1.race,
                    eventPlayer2.race,
                    nextEvent.match.mapName,
                    eventPlayer1.won);
                p2.AddMapWin(blizzardInfoPlayer2, eventPlayer2.race,
                    eventPlayer1.race,
                    nextEvent.match.mapName,
                    eventPlayer2.won);

                await _playerRepository.UpsertPlayerHeroStats(p1);
                await _playerRepository.UpsertPlayerHeroStats(p2);
            }
        }
    }
}