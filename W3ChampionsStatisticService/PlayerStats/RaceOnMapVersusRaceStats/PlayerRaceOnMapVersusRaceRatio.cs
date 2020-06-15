using System.Collections.Generic;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats
{
    public class PlayerRaceOnMapVersusRaceRatio : IIdentifiable
    {
        public static PlayerRaceOnMapVersusRaceRatio Create(string battleTag, int season)
        {
            return new PlayerRaceOnMapVersusRaceRatio
            {
                Id = $"{season}_{battleTag}",
                BattleTag = battleTag,
                Season = season
            };
        }

        public string Id { get; set; }
        public MapWinsPerRaceList RaceWinsOnMap { get; set; } = MapWinsPerRaceList.Create();

        public Dictionary<string, MapWinsPerRaceList> RaceWinsOnMapByPatch { get; set; }

        public string BattleTag { get; set; }
        public int Season { get; set; }

        public void AddMapWin(Race myRace, Race enemyRace, string mapName, bool won, string patch)
        {
            if(RaceWinsOnMapByPatch == null){
                RaceWinsOnMapByPatch = new Dictionary<string, MapWinsPerRaceList>();
            }

            if(!RaceWinsOnMapByPatch.ContainsKey(patch)){
                RaceWinsOnMapByPatch[patch] = MapWinsPerRaceList.Create();
            }

            //Add win to stats by patch
            RaceWinsOnMapByPatch[patch].AddWin(myRace, enemyRace, mapName, won);

            //Add win to overall stats by season
            // RaceWinsOnMap.AddWin(myRace, enemyRace, mapName, won);
        }
    }
}