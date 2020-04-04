using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapStats
{
    public class RaceOnMapRatio
    {
        public static RaceOnMapRatio Create(string battleTag)
        {
            return new RaceOnMapRatio
            {
                Id = battleTag
            };
        }

        public MapWinRatio RaceWinRatio { get; set; } = MapWinRatio.Create();
        public string Id { get; set; }

        public void AddMapWin(Race myRace, string map, bool won)
        {
            RaceWinRatio.RecordWin(myRace, map, won);
        }
    }
}