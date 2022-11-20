using System;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats
{
    public class PlayerMmrRpTimelineHandler : IReadModelHandler
    {
        private readonly IPlayerRepository _playerRepository;

        public PlayerMmrRpTimelineHandler(
            IPlayerRepository playerRepository
            )
        {
            _playerRepository = playerRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent) 
        {
            var match = nextEvent.match;

            foreach (var player in match.players)
            {
                if (player.updatedMmr == null || match.endTime == 0) { return; }
                var mmrRpTimeline = await _playerRepository.LoadPlayerMmrRpTimeline(player.battleTag, player.race, match.gateway, match.season, match.gameMode)
                           ?? new PlayerMmrRpTimeline(player.battleTag, player.race, match.gateway, match.season, match.gameMode);
                mmrRpTimeline.UpdateTimeline(new MmrRpAtDate(
                    mmr: (int)player.updatedMmr.rating,
                    rp: player.ranking?.rp,
                    date: DateTimeOffset.FromUnixTimeMilliseconds(match.endTime)));;
                await _playerRepository.UpsertPlayerMmrRpTimeline(mmrRpTimeline);
            }
        }
    }
}
