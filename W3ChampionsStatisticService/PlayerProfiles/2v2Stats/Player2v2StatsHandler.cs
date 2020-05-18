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
            var match = nextEvent.match;
            if (match.gameMode != GameMode.GM_2v2_AT)
            {
                return;
            }

            var winners = match.players.Where(p => p.won).ToList();
            var loosers = match.players.Where(p => !p.won).ToList();

            var winnerId = new BattleTagIdCombined(
                winners.Select(w => PlayerId.Create(w.battleTag)).ToList(),
                match.gateway,
                match.gameMode,
                match.season);

            var looserId = new BattleTagIdCombined(
                loosers.Select(w => PlayerId.Create(w.battleTag)).ToList(),
                match.gateway,
                match.gameMode,
                match.season);

            var winner = await _playerRepository.LoadTeamStat(winnerId.Id) ?? At2V2StatsPerGateway.Create(winnerId);
            var looser = await _playerRepository.LoadTeamStat(looserId.Id) ?? At2V2StatsPerGateway.Create(looserId);

            winner.RecordWin(true);
            looser.RecordWin(false);

            winner.RecordRanking(
                (int?) winners.First().updatedMmr?.rating ?? (int?) winners.First().mmr?.rating ?? 0,
                (int?) winners.First().updatedRanking?.rp ?? (int?) winners.First().ranking?.rp ?? 0);

            looser.RecordRanking(
                (int?) loosers.First().updatedMmr?.rating ?? (int?) loosers.First().mmr?.rating ?? 0,
                (int?) loosers.First().updatedRanking?.rp ?? (int?) loosers.First().ranking?.rp ?? 0);

            await _playerRepository.UpsertTeamStat(winner);
            await _playerRepository.UpsertTeamStat(looser);
        }
    }
}