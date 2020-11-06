using System;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats
{
    public class PlayerMmrTimelineHandler : IReadModelHandler
    {
        private readonly IPlayerRepository _playerRepository;
        private readonly IMatchRepository _matchRepository;

        public PlayerMmrTimelineHandler(
            IPlayerRepository playerRepository,
            IMatchRepository matchRepository
            )
        {
            _playerRepository = playerRepository;
            _matchRepository = matchRepository;
        }

        public async Task Update(MatchFinishedEvent nextEvent) 
        {
            var match = nextEvent.match;

            foreach (var player in match.players)
            {
                var mmrTimeline = await _playerRepository.LoadPlayerMmrTimeline(player.battleTag, player.race, match.gateway, match.season)
                           ?? await CreateMmrTimeline(match, player);

                // Unclear: When previous line used CreateMmrTimeline(), is the new match already added to the timeline or not?
                mmrTimeline.MmrAtTimes.Add(new MmrAtTime(
                    mmr: (int)player.mmr.rating,
                    mmrTime: DateTimeOffset.FromUnixTimeMilliseconds(match.endTime)));;

                await _playerRepository.UpsertPlayerMmrTimeline(mmrTimeline);
            }
        }

        public async Task<PlayerMmrTimeline> CreateMmrTimeline(Match match, PlayerMMrChange player)
        {
            var count = await _matchRepository.CountFor(player.battleTag, null, match.gateway, match.gameMode, match.season);
            int pageSize = (int)count;
            var matches = await _matchRepository.LoadFor(player.battleTag, null, match.gateway, match.gameMode, pageSize, 0, match.season);
            PlayerMmrTimeline mmrTimeline = new PlayerMmrTimeline(player.battleTag, player.race, match.gateway, match.season);
            // Is there a better way to traverse through this?
            foreach (Matchup m in matches)
            {
                foreach(Team t in m.Teams)
                {
                    foreach (PlayerOverviewMatches p in t.Players)
                    {
                        if (p.BattleTag == player.battleTag)
                        {
                            // Is this the correct sorting?
                            mmrTimeline.MmrAtTimes.Add(new MmrAtTime(p.CurrentMmr, m.EndTime));
                        }
                    }
                }
            }
            return mmrTimeline;
        }
    }
}
