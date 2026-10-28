using System.Collections.Generic;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

public class PlayerRaceOnMapVersusRaceRatioView
{
    // MapName is stored per entry from the MatchFinishedEvent, so it is returned as is.
    public static PlayerRaceOnMapVersusRaceRatioView Create(PlayerRaceOnMapVersusRaceRatio player)
    {
        return new PlayerRaceOnMapVersusRaceRatioView
        {
            Id = player.Id,
            BattleTag = player.BattleTag,
            Season = player.Season,
            RaceWinsOnMap = player.RaceWinsOnMap,
            RaceWinsOnMapByPatch = player.RaceWinsOnMapByPatch
        };
    }

    public static PlayerRaceOnMapVersusRaceRatioView Create(string battleTag, int season)
    {
        return Create(PlayerRaceOnMapVersusRaceRatio.Create(battleTag, season));
    }

    public string Id { get; set; }
    public MapWinsPerRaceList RaceWinsOnMap { get; set; } = MapWinsPerRaceList.Create();

    public Dictionary<string, MapWinsPerRaceList> RaceWinsOnMapByPatch { get; set; }

    public string BattleTag { get; set; }
    public int Season { get; set; }
}
