using System.Collections.Generic;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.Ladder;

public static class RankExtensions
{
    public static Rank CreateUnrankedResponse(this PlayerOverallStats stats)
    {
        return new Rank([stats.BattleTag], 0, 0, 0, null, GateWay.Undefined, GameMode.Undefined, 0)
        {
            Players = new List<PlayerOverview>
            {
                PlayerOverview.Create(new List<PlayerId>
                {
                    new() {
                        BattleTag = stats.BattleTag,
                        Name = stats.Name
                    }
                }, GateWay.Undefined, GameMode.Undefined, 0, null)
            }
        };
    }
}
