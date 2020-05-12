using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PlayerStats.HeroStats
{
    public class HeroStatsItemList : List<HeroStatsItem>
    {
        public static HeroStatsItemList Create()
        {
            return new HeroStatsItemList();
        }

        public List<HeroStatsItem> HeroStats { get; set; }

        public void AddWin(string heroId, Race myRace, Race enemyRace, string mapName, in bool won)
        {
            var heroStats = this.SingleOrDefault(r => r.HeroId == heroId);
            
            if (heroStats == null)
            {
                heroStats = HeroStatsItem.Create(heroId);
                this.Add(heroStats);
            }

            heroStats.AddWin(myRace, enemyRace, mapName, won);
        }
    }
}
