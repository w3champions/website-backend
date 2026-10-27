namespace W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;

public class GamesPlayedOnMap
{
    public static GamesPlayedOnMap Create(string map)
    {
        return new GamesPlayedOnMap
        {
            Map = map
        };
    }

    public string Map { get; set; }

    public int? MapId { get; set; }

    public string MapName { get; set; }

    public void CountMatch(int? mapId, string mapName)
    {
        if (mapId != null) MapId = mapId;
        if (mapName != null) MapName = mapName;
        Count++;
    }

    public int Count { get; set; }
}
