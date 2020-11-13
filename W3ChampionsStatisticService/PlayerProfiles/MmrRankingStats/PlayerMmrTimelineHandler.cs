using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats
{
    public class PlayerMmrTimelineHandler : IReadModelHandler
    {
        private readonly IPlayerRepository _playerRepository;

        public PlayerMmrTimelineHandler(
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
                var mmrTimeline = await _playerRepository.LoadPlayerMmrTimeline(player.battleTag, player.race, match.gateway, match.season, match.gameMode)
                           ?? new PlayerMmrTimeline(player.battleTag, player.race, match.gateway, match.season, match.gameMode);
            mmrTimeline.AddSorted(new MmrAtTime(
            mmr: (int)player.updatedMmr.rating,
                    mmrTime: DateTimeOffset.FromUnixTimeMilliseconds(match.endTime)));;
                await _playerRepository.UpsertPlayerMmrTimeline(mmrTimeline);
            }
        }
    }
}
