using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats
{
    public class OverallRaceAndWinStat
    {
        public int MmrRange { get; set; }

        public OverallRaceAndWinStat(int mmrRange)
        {
            MmrRange = mmrRange;
        }

        public string Id => $"MMR_{MmrRange.ToString()}";

        public List<MapToRaceVsRaceRatio> StatsPerModes { get; set; } = new List<MapToRaceVsRaceRatio>();

        public void Apply(string mapName, Race homeRace, Race enemyRas, bool won)
        {
            var stats = StatsPerModes.SingleOrDefault(s => s.MapName == mapName);
            if (stats == null)
            {
                StatsPerModes.Add(MapToRaceVsRaceRatio.Create(mapName));
            }

            var statsForSure = StatsPerModes.Single(s => s.MapName == mapName);
            statsForSure.Ratio.RecordWin(homeRace, enemyRas, won);
        }
    }
}