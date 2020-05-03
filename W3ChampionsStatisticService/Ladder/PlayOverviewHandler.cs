using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Ladder
{
    public class PlayOverviewHandler : IReadModelHandler
    {
        private readonly IPlayerRepository _playerRepository;

        public PlayOverviewHandler(
            IPlayerRepository playerRepository
            )
        {
            _playerRepository = playerRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            foreach (var playerRaw in nextEvent.match.players)
            {
                if (nextEvent.match.gameMode == GameMode.GM_1v1)
                {
                    var player = await _playerRepository.LoadOverview(playerRaw.id)
                                 ?? PlayerOverview1v1.Create(
                                     playerRaw.id,
                                     playerRaw.battleTag,
                                     nextEvent.match.gateway,
                                     nextEvent.match.gameMode);
                    player.RecordWin(
                        playerRaw.won,
                        (int?) playerRaw.updatedMmr?.rating ?? (int) playerRaw.mmr.rating);
                    await _playerRepository.UpsertPlayer(player);
                }

                if (nextEvent.match.gameMode == GameMode.GM_2v2_AT)
                {
                    //todo
                }
            }
        }
    }
}