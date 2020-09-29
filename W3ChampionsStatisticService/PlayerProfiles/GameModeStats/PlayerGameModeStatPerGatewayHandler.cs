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

            if ((nextEvent.match.gameMode == GameMode.GM_2v2
                 || nextEvent.match.gameMode == GameMode.GM_2v2_AT)
                && winners.Count != 2 && losers.Count != 2 )
            {
                return;
            }

            // some events are buggy
            if (winners.Count != losers.Count && match.gameMode != GameMode.FFA) return;

            await RecordWinners(match, winners);

            await RecordLosers(match, losers);
        }

        private async Task RecordLosers(Match match, List<PlayerMMrChange> losers)
        {
            List<PlayerMMrChange> processed = new List<PlayerMMrChange>();

            if (match.gameMode.IsRandomTeam())
            {
                foreach (var losingPlayer in losers.Where(x => !x.IsAt))
                {
                    await RecordLoss(match, new List<PlayerMMrChange>() { losingPlayer });
                    processed.Add(losingPlayer);
                }
            }

            foreach (var losingTeam in losers.Where(x => !processed.Contains(x)).GroupBy(x => x.team))
            {
                await RecordLoss(match, losingTeam.ToList());
            }
        }

        private async Task RecordLoss(Match match, List<PlayerMMrChange> losingTeam)
        {
            var gameMode = GetGameModeStatGameMode(match.gameMode, losingTeam[0]);

            var loserId = new BattleTagIdCombined(
              losingTeam.Select(w => PlayerId.Create(w.battleTag)).ToList(),
              match.gateway,
              gameMode,
              match.season,
              match.gameMode == GameMode.GM_1v1 && match.season >= 2 ? (Race?) losingTeam.Single().race : null);

            var loser = await _playerRepository.LoadGameModeStatPerGateway(loserId.Id) ?? PlayerGameModeStatPerGateway.Create(loserId);

            loser.RecordWin(false);

            var firstLooser = losingTeam.First();

            loser.RecordRanking(
                (int?)firstLooser.updatedMmr?.rating ?? (int?)firstLooser.mmr?.rating ?? 0,
                (int?)firstLooser.updatedRanking?.rp ?? (int?)firstLooser.ranking?.rp ?? 0);

            await _playerRepository.UpsertPlayerGameModeStatPerGateway(loser);
        }

        private async Task RecordWinners(Match match, List<PlayerMMrChange> winners)
        {
            List<PlayerMMrChange> processed = new List<PlayerMMrChange>();

            if (match.gameMode.IsRandomTeam())
            {
                foreach (var winningPlayer in winners.Where(x => !x.IsAt))
                {
                    await RecordWin(match, new List<PlayerMMrChange>() { winningPlayer });
                    processed.Add(winningPlayer);
                }
            }

            foreach (var winningTeam in winners.Where(x => !processed.Contains(x)).GroupBy(x => x.team))
            {
                await RecordWin(match, winningTeam.ToList());
            }
        }

        private async Task RecordWin(Match match, List<PlayerMMrChange> winners)
        {
            var winnerId = new BattleTagIdCombined(
                winners.Select(w => PlayerId.Create(w.battleTag)).ToList(),
                match.gateway,
                match.gameMode,
                match.season,
                match.gameMode == GameMode.GM_1v1 && match.season >= 2 ? (Race?) winners.Single().race : null);

            var winner = await _playerRepository.LoadGameModeStatPerGateway(winnerId.Id) ?? PlayerGameModeStatPerGateway.Create(winnerId);
            winner.RecordWin(true);
            winner.RecordRanking(
                (int?)winners.First().updatedMmr?.rating ?? (int?)winners.First().mmr?.rating ?? 0,
                (int?)winners.First().updatedRanking?.rp ?? (int?)winners.First().ranking?.rp ?? 0);

            await _playerRepository.UpsertPlayerGameModeStatPerGateway(winner);
        }

        private GameMode GetGameModeStatGameMode(GameMode gameMode, PlayerMMrChange player)
        {
            if (gameMode == GameMode.GM_2v2 && player.IsAt)
            {
                return GameMode.GM_2v2_AT;
            }

            return gameMode;
        }
    }
}