using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats;

public class HeroPlayedStat : IIdentifiable
{
    public static HeroPlayedStat Create()
    {
        return new HeroPlayedStat
        {
            Stats =
            [
                new() { GameMode = GameMode.GM_1v1 },
                new() { GameMode = GameMode.FFA },
                new() { GameMode = GameMode.GM_4v4 },
                new() { GameMode = GameMode.GM_2v2_AT },
                new() { GameMode = GameMode.GM_2v2 },
                new() { GameMode = GameMode.GM_4v4_AT },
                new() { GameMode = GameMode.GM_DOTA_5ON5 },
            ]
        };
    }

    public List<HeroStatByMode> Stats { get; set; }

    public void AddHeroes(List<HeroPickDto> heroes, GameMode gameMode)
    {
        var total = Stats.SingleOrDefault(s => s.GameMode == gameMode);
        if (total != null)
        {
            total.AddHeroes(heroes);
        }
    }
    public string Id { get; set; } = nameof(HeroPlayedStat);
}
