using System.Collections.Generic;
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
            var players = new List<Player>();
            foreach (var playerId in nextEvent.data.players)
            {
                var player = await _playerRepository.Load(playerId.battleTag);
                players.Add(player ?? new Player(playerId.battleTag));
            }

            foreach (var player in players)
            {
                player.UpdateProgress(nextEvent);
                await _playerRepository.Upsert(player);
            }
        }
    }
}