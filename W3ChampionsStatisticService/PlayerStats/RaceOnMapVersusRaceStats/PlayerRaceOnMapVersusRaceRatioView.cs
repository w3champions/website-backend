using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats
{
    public class PlayerRaceOnMapVersusRaceRatioView
    {
        public static PlayerRaceOnMapVersusRaceRatioView Create(PlayerRaceOnMapVersusRaceRatio player, SeasonMapInformation mapNames)
        {
            var mapVersusRaceRatioView = new PlayerRaceOnMapVersusRaceRatioView
            {
                Id = player.Id,
                BattleTag = player.BattleTag,
                Season = player.Season,
                RaceWinsOnMap = player.RaceWinsOnMap,
                RaceWinsOnMapByPatch = player.RaceWinsOnMapByPatch
            };

            foreach (var winLossesPerMap in mapVersusRaceRatioView.RaceWinsOnMap
                         .SelectMany(x => x.WinLossesOnMap))
            {
                winLossesPerMap.MapName = mapNames.GetMapName(winLossesPerMap.Map);
            }

            foreach (var winLossesPerMap in mapVersusRaceRatioView.RaceWinsOnMapByPatch
                         .SelectMany(x => x.Value)
                         .SelectMany(x => x.WinLossesOnMap))
            {
                winLossesPerMap.MapName = mapNames.GetMapName(winLossesPerMap.Map);
            }

            return mapVersusRaceRatioView;
        }

        public static PlayerRaceOnMapVersusRaceRatioView Create(string battleTag, int season)
        {
            return Create(PlayerRaceOnMapVersusRaceRatio.Create(battleTag, season), SeasonMapInformation.Empty);
        }

        public string Id { get; set; }
        public MapWinsPerRaceList RaceWinsOnMap { get; set; } = MapWinsPerRaceList.Create();

        public Dictionary<string, MapWinsPerRaceList> RaceWinsOnMapByPatch { get; set; }

        public string BattleTag { get; set; }
        public int Season { get; set; }
    }
}