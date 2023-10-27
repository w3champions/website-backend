using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats;

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
