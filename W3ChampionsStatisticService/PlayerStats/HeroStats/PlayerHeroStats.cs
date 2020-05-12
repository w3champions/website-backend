using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Extensions;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.PlayerStats.HeroStats
{
    public class PlayerHeroStats
    {
        public static PlayerHeroStats Create(string battleTag, int season)
        {
            return new PlayerHeroStats
            {
                Id = battleTag,
                Season = season
            };
        }

        public HeroStatsItemList HeroStatsItemList { get; set; } = HeroStatsItemList.Create();
        public string Id { get; set; }
        public int Season { get; set; }

        public void AddMapWin(PlayerBlizzard playerBlizzard, Race myRace, Race enemyRace, string mapName, bool won)
        {
            if (playerBlizzard.heroes == null)
            {
                return;
            }

            foreach (var hero in playerBlizzard.heroes)
            {
                HeroStatsItemList.AddWin(hero.icon.ParseReforgedName(), myRace, enemyRace, mapName, won);
            }
        }
    }
}
