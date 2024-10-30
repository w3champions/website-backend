using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;

public class MapsPerSeason : IIdentifiable
{
    public List<MatchOnMapPerMode> MatchesOnMapPerModes { get; set; } = new List<MatchOnMapPerMode>();

    [JsonIgnore]
    public string Id => Season.ToString();

    public static MapsPerSeason Create(int season)
    {
        return new MapsPerSeason
        {
            Season = season
        };
    }

    public int Season { get; set; }

    [Trace]
    public void Count(string map, string mapName, GameMode gameMode)
    {
        var matchOnMapOverall = MatchesOnMapPerModes.SingleOrDefault(m => m.GameMode == GameMode.Undefined);
        if (matchOnMapOverall == null)
        {
            MatchesOnMapPerModes.Add(MatchOnMapPerMode.Create(GameMode.Undefined));
        }

        matchOnMapOverall = MatchesOnMapPerModes.Single(m => m.GameMode == GameMode.Undefined);
        matchOnMapOverall.CountMatch(map, mapName);

        var matchOnMap = MatchesOnMapPerModes.SingleOrDefault(m => m.GameMode == gameMode);
        if (matchOnMap == null)
        {
            MatchesOnMapPerModes.Add(MatchOnMapPerMode.Create(gameMode));
        }

        matchOnMap = MatchesOnMapPerModes.Single(m => m.GameMode == gameMode);
        matchOnMap.CountMatch(map, mapName);
    }
    
    // Return true if updates were made
    public bool UpdateMapName(string map, string mapName, GameMode gameMode)
    {
        var matchOnMapOverall = MatchesOnMapPerModes.SingleOrDefault(m => m.GameMode == GameMode.Undefined);
        var matchOnMap = MatchesOnMapPerModes.SingleOrDefault(m => m.GameMode == gameMode);
        if (matchOnMapOverall == null || matchOnMap == null) {
            return false;
        }

        bool onMapUpdated = matchOnMap.UpdateMapName(map, mapName);
        bool overallUpdated = matchOnMapOverall.UpdateMapName(map, mapName);

        return overallUpdated && onMapUpdated;
    }
}
