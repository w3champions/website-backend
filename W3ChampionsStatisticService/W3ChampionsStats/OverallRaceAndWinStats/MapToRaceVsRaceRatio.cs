using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats;

public class MapToRaceVsRaceRatio
{
    public string Map { get; set; }
    public int? MapId { get; set; }
    public string MapName { get; set; }
    public RaceWinRatio Ratio { get; set; } = RaceWinRatio.Create();

    public static MapToRaceVsRaceRatio Create(string map)
    {
        return new MapToRaceVsRaceRatio
        {
            Map = map
        };
    }

    public void UpdateMapInfo(int? mapId, string mapName)
    {
        if (mapId != null) MapId = mapId;
        if (mapName != null) MapName = mapName;
    }
}
