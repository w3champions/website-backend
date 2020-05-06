using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
            var winners = nextEvent.match.players.Where(p => p.won).ToList();
            var loosers = nextEvent.match.players.Where(p => !p.won).ToList();

            // for broken events
            if (winners.Count == 0 || loosers.Count == 0) return;

            var winner = await UpdatePlayers(nextEvent, winners);
            var looser = await UpdatePlayers(nextEvent, loosers);

            await _playerRepository.UpsertPlayer(winner);
            await _playerRepository.UpsertPlayer(looser);
        }

        private async Task<PlayerOverview> UpdatePlayers(MatchFinishedEvent nextEvent, List<PlayerMMrChange> players)
        {
            var winnerPlayerIds = players.Select(w => PlayerId.Create(w.battleTag)).ToList();

            var winnerIdCombined =
                $"{string.Join("_", winnerPlayerIds.OrderBy(t => t.BattleTag).Select(t => $"{t.BattleTag}@{nextEvent.match.gateway}"))}_{nextEvent.match.gameMode}";

            var winner = await _playerRepository.LoadOverview(winnerIdCombined)
                         ?? PlayerOverview.Create(
                             winnerPlayerIds,
                             nextEvent.match.gateway,
                             nextEvent.match.gameMode);

            winner.RecordWin(
                players.First().won,
                (int?) players.First().updatedMmr?.rating ?? (int?) players.First().mmr?.rating ?? 0);

            return winner;
        }
    }
}