using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace W3ChampionsStatisticService.PlayerStats.HeroStats
{
    public class HeroStatsItem
    {
        public static HeroStatsItem Create(string heroId)
        {
            return new HeroStatsItem
            {
                HeroId = heroId,
                Stats = MapWinsPerRaceList.Create()
            };
        }

        public string HeroId { get; set; }

        public MapWinsPerRaceList Stats { get; set; }

        public void AddWin(Race myRace, Race enemyRace, string mapName, in bool won)
        {
            Stats.AddWin(myRace, enemyRace, mapName, won);
        }
    }
}
