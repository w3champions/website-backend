using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
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
            var losers = nextEvent.match.players.Where(p => !p.won).ToList();

            if (winners.Count == 0 || losers.Count == 0)
            {
                // We should log the bad event here
                return;
            }

            if ((nextEvent.match.gameMode == GameMode.GM_2v2
                 || nextEvent.match.gameMode == GameMode.GM_2v2_AT)
                && winners.Count != 2 && losers.Count != 2)
            {
                return;
            }

            // for broken events
            if (winners.Count == 0 || losers.Count == 0) return;

            await UpdateWinners(nextEvent, winners);

            await UpdateLosers(nextEvent, losers);
        }

        private async Task UpdateWinners(MatchFinishedEvent nextEvent, List<PlayerMMrChange> winners)
        {
            if (nextEvent.match.gameMode.IsRandomTeam())
            {
                foreach (var winningPlayer in winners)
                {
                    var winner = await UpdatePlayers(nextEvent, new List<PlayerMMrChange>() { winningPlayer });
                    await _playerRepository.UpsertPlayerOverview(winner);
                }
            }
            else
            {
                foreach (var winningPlayer in winners.GroupBy(x => x.team))
                {
                    var winner = await UpdatePlayers(nextEvent, winningPlayer.ToList());
                    await _playerRepository.UpsertPlayerOverview(winner);
                }
            }
        }

        private async Task UpdateLosers(MatchFinishedEvent nextEvent, List<PlayerMMrChange> losers)
        {
            if (nextEvent.match.gameMode.IsRandomTeam())
            {
                foreach (var losingPlayer in losers)
                {
                    var loser = await UpdatePlayers(nextEvent, new List<PlayerMMrChange>() { losingPlayer });
                    await _playerRepository.UpsertPlayerOverview(loser);
                }
            }
            else
            {
                foreach (var losingTeam in losers.GroupBy(x => x.team))
                {
                    var loser = await UpdatePlayers(nextEvent, losingTeam.ToList());
                    await _playerRepository.UpsertPlayerOverview(loser);
                }
            }
        }

        private async Task<PlayerOverview> UpdatePlayers(MatchFinishedEvent nextEvent, List<PlayerMMrChange> players)
        {
            var playerIds = players.Select(w => PlayerId.Create(w.battleTag)).ToList();

            var match = nextEvent.match;
            var playerRaceIfSingle = match.gameMode == GameMode.GM_1v1 && match.season >= 2 ? (Race?) players.Single().race : null;
            var winnerIdCombined = new BattleTagIdCombined(
                players.Select(p =>
                    PlayerId.Create(p.battleTag)).ToList(),
                        match.gateway,
                        match.gameMode,
                        match.season,
                        playerRaceIfSingle);

            var winner = await _playerRepository.LoadOverview(winnerIdCombined.Id)
                         ?? PlayerOverview.Create(
                             playerIds,
                             match.gateway,
                             match.gameMode,
                             match.season,
                             playerRaceIfSingle);

            winner.RecordWin(
                players.First().won,
                (int?) players.First().updatedMmr?.rating ?? (int?) players.First().mmr?.rating ?? 0);

            return winner;
        }
    }
}