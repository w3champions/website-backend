using W3ChampionsStatisticService.PlayerStats.RaceVersusRaceStats;

namespace W3ChampionsStatisticService.W3ChampionsStats.RaceAndWinStats
{
    public class MapToRaceVsRaceRatio
    {
        public string MapName { get; set; }
        public RaceVersusRaceRatio Ratio { get; set; }

        public static MapToRaceVsRaceRatio Create(string mapName)
        {
            return new MapToRaceVsRaceRatio
            {
                MapName = mapName
            };
        }
    }
}