using MongoDB.Bson.Serialization.Attributes;

namespace W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;

public class GamesPlayedOnMap
{
    public static GamesPlayedOnMap Create(string map, string mapName)
    {
        return new GamesPlayedOnMap
        {
            Map = map,
            MapName = mapName
        };
    }

    public string Map { get; set; }
    public string MapName { get; set; }

    public void CountMatch()
    {
        Count++;
    }

    public int Count { get; set; }
}
