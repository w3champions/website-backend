using System.Collections.Generic;
using System.Linq;

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
                RaceWinsOnMapByPatch = battleTag.RaceWinsOnMapByPatch
            };

            foreach (var winLossesPerMap in mapVersusRaceRatioView.RaceWinsOnMap
                         .SelectMany(x => x.WinLossesOnMap))
            {
                if (mapNames.TryGetValue(winLossesPerMap.Map, out var mapName))
                {
                    winLossesPerMap.MapName = mapName;
                }
            }

            foreach (var winLossesPerMap in mapVersusRaceRatioView.RaceWinsOnMapByPatch
                         .SelectMany(x => x.Value)
                         .SelectMany(x => x.WinLossesOnMap))
            {
                if (mapNames.TryGetValue(winLossesPerMap.Map, out var mapName))
                {
                    winLossesPerMap.MapName = mapName;
                }
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