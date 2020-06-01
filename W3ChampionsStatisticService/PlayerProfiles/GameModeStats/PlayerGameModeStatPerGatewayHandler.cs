using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerProfiles.GameModeStats
{
    public class PlayerGameModeStatPerGatewayHandler : IReadModelHandler
    {
        private readonly IPlayerRepository _playerRepository;

        public PlayerGameModeStatPerGatewayHandler(
            IPlayerRepository playerRepository
            )
        {
            _playerRepository = playerRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent)
        {
            var match = nextEvent.match;

            var winners = match.players.Where(p => p.won).ToList();

            var losers = match.players.Where(p => !p.won).ToList();

            if (winners.Count == 0 || losers.Count == 0)
            {
                // We should log the bad event here
                return;
            }

            // some events are buggy
            if (winners.Count != losers.Count && match.gameMode != GameMode.FFA) return;

            await RecordWinners(match, winners);

            await RecordLosers(match, losers);
        }

        private async Task RecordLosers(Match match, List<PlayerMMrChange> losers)
        {
            foreach (var losingTeam in losers.GroupBy(x => x.team))
            {
                var loserId = new BattleTagIdCombined(
                  losingTeam.Select(w => PlayerId.Create(w.battleTag)).ToList(),
                  match.gateway,
                  match.gameMode,
                  match.season);

                var loser = await _playerRepository.LoadGameModeStatPerGateway(loserId.Id) ?? PlayerGameModeStatPerGateway.Create(loserId);

                loser.RecordWin(false);

                var firstLooser = losingTeam.First();

                loser.RecordRanking(
                    (int?)firstLooser.updatedMmr?.rating ?? (int?)firstLooser.mmr?.rating ?? 0,
                    (int?)firstLooser.updatedRanking?.rp ?? (int?)firstLooser.ranking?.rp ?? 0);

                await _playerRepository.UpsertPlayerGameModeStatPerGateway(loser);
            }
        }

        private async Task RecordWinners(Match match, List<PlayerMMrChange> winners)
        {
            var winnerId = new BattleTagIdCombined(
                winners.Select(w => PlayerId.Create(w.battleTag)).ToList(),
                match.gateway,
                match.gameMode,
                match.season);

            var winner = await _playerRepository.LoadGameModeStatPerGateway(winnerId.Id) ?? PlayerGameModeStatPerGateway.Create(winnerId);
            winner.RecordWin(true);
            winner.RecordRanking(
                (int?)winners.First().updatedMmr?.rating ?? (int?)winners.First().mmr?.rating ?? 0,
                (int?)winners.First().updatedRanking?.rp ?? (int?)winners.First().ranking?.rp ?? 0);

            await _playerRepository.UpsertPlayerGameModeStatPerGateway(winner);
        }
    }
}