using W3ChampionsStatisticService.PlayerStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace W3ChampionsStatisticService.W3ChampionsStats.RaceAndWinStats
{
    public class MapToRaceVsRaceRatio
    {
        public string MapName { get; set; }
        public RaceWinRatio Ratio { get; set; } = RaceWinRatio.Create();

        public static MapToRaceVsRaceRatio Create(string mapName)
        {
            return new MapToRaceVsRaceRatio
            {
                MapName = mapName
            };
        }
    }
}