using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats
{
    public class PlayerRaceOnMapVersusRaceRatio
    {
        public static PlayerRaceOnMapVersusRaceRatio Create(string battleTag, int season)
        {
            return new PlayerRaceOnMapVersusRaceRatio
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