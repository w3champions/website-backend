using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerProfiles._2v2Stats
{
    public class Player2v2StatsHandler : IReadModelHandler
    {
        private readonly IPlayerRepository _playerRepository;

        public Player2v2StatsHandler(
            IPlayerRepository playerRepository
            )
        {
            _playerRepository = playerRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            if (nextEvent.match.gameMode != GameMode.GM_2v2_AT)
            {
                return;
            }

            var winners = nextEvent.match.players.Where(p => p.won);
            var loosers = nextEvent.match.players.Where(p => !p.won);
        }
    }
}