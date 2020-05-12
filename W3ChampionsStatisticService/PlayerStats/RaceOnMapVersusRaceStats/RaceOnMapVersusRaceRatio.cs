using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats
{
    public class RaceOnMapVersusRaceRatio
    {
        public static RaceOnMapVersusRaceRatio Create(string battleTag, int season)
        {
            return new RaceOnMapVersusRaceRatio
            {
                Id = battleTag,
                Season = season
            };
        }

        public MapWinsPerRaceList RaceWinsOnMap { get; set; } = MapWinsPerRaceList.Create();
        public string Id { get; set; }
        public int Season { get; set; }

        public void AddMapWin(Race myRace, Race enemyRace, string mapName, bool won)
        {
            RaceWinsOnMap.AddWin(myRace, enemyRace, mapName, won);
        }
    }
}