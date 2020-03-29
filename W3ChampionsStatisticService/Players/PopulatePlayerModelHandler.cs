using System.Threading.Tasks;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Players
{
    public class PopulatePlayerModelHandler : IReadModelHandler
    {
        private readonly IPlayerRepository _playerRepository;

        public PopulatePlayerModelHandler(
            IPlayerRepository playerRepository
            )
        {
            _playerRepository = playerRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            await _playerRepository.Insert(new Player(nextEvent));
        }
    }
}