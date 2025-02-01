using Serilog;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.PlayerStats.HeroStats;

public class PlayerHeroStats : IIdentifiable
{
    public static PlayerHeroStats Create(string battleTag, int season)
    {
        return new PlayerHeroStats
        {
            Id = $"{season}_{battleTag}",
            BattleTag = battleTag,
            Season = season
        };
    }

    public string Id { get; set; }

    public HeroStatsItemList HeroStatsItemList { get; set; } = HeroStatsItemList.Create();
    public string BattleTag { get; set; }
    public int Season { get; set; }

    public void AddMapWin(PlayerBlizzard playerBlizzard, Race myRace, Race enemyRace, string mapName, bool won)
    {
        if (playerBlizzard.heroes is { Count: > 0 })
        {
            foreach (var hero in playerBlizzard.heroes)
            {
                if (hero == null)
                {
                    Log.Error("Got null hero for {battleTag}", playerBlizzard.battleTag);
                    continue;
                }
                HeroStatsItemList.AddWin(hero.icon.ParseReforgedName(), myRace, enemyRace, mapName, won);
            }
        }
    }
}
