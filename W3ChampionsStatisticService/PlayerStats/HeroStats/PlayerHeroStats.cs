using W3ChampionsStatisticService.Extensions;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PlayerStats.HeroStats
{
    public class PlayerHeroStats
    {
        public static PlayerHeroStats Create(string battleTag)
        {
            return new PlayerHeroStats
            {
                Id = battleTag
            };
        }

        public HeroStatsItemList HeroStatsItemList { get; set; } = HeroStatsItemList.Create();
        public string Id { get; set; }

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
