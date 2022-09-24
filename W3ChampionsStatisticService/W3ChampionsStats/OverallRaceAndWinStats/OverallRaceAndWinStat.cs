using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;

namespace W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats
{
    public class OverallRaceAndWinStat
    {
        public int MmrRange { get; set; }

        public OverallRaceAndWinStat(int mmrRange)
        {
            MmrRange = mmrRange;
            PatchToStatsPerModes = new Dictionary<string, List<MapToRaceVsRaceRatio>>();
        }

        public int Id => MmrRange;

        public Dictionary<string, List<MapToRaceVsRaceRatio>> PatchToStatsPerModes { get; set; }

        public List<MapToRaceVsRaceRatio> StatsPerModes { get; set; } = new List<MapToRaceVsRaceRatio>();

        public void Apply(string mapName, Race homeRace, Race enemyRas, bool won, string patch)
        {
            if (PatchToStatsPerModes == null)
            {
                PatchToStatsPerModes = new Dictionary<string, List<MapToRaceVsRaceRatio>>();
            }

            if (!PatchToStatsPerModes.ContainsKey(patch))
            {
                PatchToStatsPerModes[patch] = new List<MapToRaceVsRaceRatio>();
            }

            var stats = PatchToStatsPerModes[patch].SingleOrDefault(s => s.MapName == mapName);

            if (stats == null)
            {
                PatchToStatsPerModes[patch].Add(MapToRaceVsRaceRatio.Create(mapName));
            }

            var statsForSure = PatchToStatsPerModes[patch].Single(s => s.MapName == mapName);
            statsForSure.Ratio.RecordWin(homeRace, enemyRas, won);
        }
    }
}