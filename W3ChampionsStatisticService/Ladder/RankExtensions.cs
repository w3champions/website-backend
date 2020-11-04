using System.Collections.Generic;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.Ladder
{
    public static class RankExtensions
    {
        public static Rank CreateUnrankedResponse(this PlayerOverallStats stats)
        {
            return new Rank(
                new List<string> { stats.BattleTag },
                0,
                0,
                0,
                null,
                GateWay.Undefined,
                GameMode.Undefined,
                0
                )
            {
                Players = new List<PlayerOverview>
                {
                    PlayerOverview.Create(new List<PlayerId>
                    {
                        new PlayerId
                        {
                            BattleTag = stats.BattleTag,
                            Name = stats.Name
                        }
                    }, GateWay.Undefined, GameMode.Undefined, 0, null)
                }
            };
        }
    }
}
