using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;

public class MatchOnMapPerMode
{
    public static MatchOnMapPerMode Create(GameMode gameMode)
    {
        return new MatchOnMapPerMode
        {
            GameMode = gameMode
        };
    }

    public GameMode GameMode { get; set; }

    public void CountMatch(string map, string mapName)
    {
        var gamesOnMode = Maps.SingleOrDefault(g => g.Map == map);
        if (gamesOnMode == null)
        {
            Maps.Add(GamesPlayedOnMap.Create(map, mapName));
        }

        gamesOnMode = Maps.Single(g => g.Map == map);
        Maps = Maps.OrderBy(m => m.Map).ToList();
        gamesOnMode.CountMatch();
    }

    // Return true if an update was made
    public bool UpdateMapName(string map, string mapName) {
        var gamesOnMode = Maps.SingleOrDefault(g => g.Map == map);
        if (gamesOnMode == null) {
            // No record of this map
            return false;
        }

        gamesOnMode = Maps.Single(g => g.Map == map);
        if (gamesOnMode.MapName != null && gamesOnMode.MapName.Equals(mapName)) {
            // MapName is already up to date
            return false;
        }
        gamesOnMode.MapName = mapName;
        return true;
    }

    public List<GamesPlayedOnMap> Maps { get; set; } = new List<GamesPlayedOnMap>();
}
