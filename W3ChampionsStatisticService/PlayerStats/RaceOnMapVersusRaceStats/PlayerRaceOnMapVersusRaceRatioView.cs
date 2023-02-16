using System.Collections.Generic;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats
{
    public class PlayerRaceOnMapVersusRaceRatioView
    {
        public static PlayerRaceOnMapVersusRaceRatioView Create(PlayerRaceOnMapVersusRaceRatio battleTag, IReadOnlyDictionary<string, string> mapNames)
        {
            var mapVersusRaceRatioView = new PlayerRaceOnMapVersusRaceRatioView
            {
                Id = battleTag.Id,
                BattleTag = battleTag.BattleTag,
                Season = battleTag.Season,
                RaceWinsOnMap = battleTag.RaceWinsOnMap,
                RaceWinsOnMapByPatch = new Dictionary<string, MapWinsPerRaceList>()
            };
            foreach (var (key, value) in battleTag.RaceWinsOnMapByPatch)
            {
                var displayMapName = mapNames.TryGetValue(key, out var mapName) ? mapName : key;
                mapVersusRaceRatioView.RaceWinsOnMapByPatch[displayMapName] = value;
            }
            return mapVersusRaceRatioView;
        }

        public static PlayerRaceOnMapVersusRaceRatioView Create(string battleTag, int mapNames)
        {
            return Create(PlayerRaceOnMapVersusRaceRatio.Create(battleTag, mapNames), new Dictionary<string, string>());
        }

        public string Id { get; set; }
        public MapWinsPerRaceList RaceWinsOnMap { get; set; } = MapWinsPerRaceList.Create();

        public Dictionary<string, MapWinsPerRaceList> RaceWinsOnMapByPatch { get; set; }

        public string BattleTag { get; set; }
        public int Season { get; set; }
    }
}