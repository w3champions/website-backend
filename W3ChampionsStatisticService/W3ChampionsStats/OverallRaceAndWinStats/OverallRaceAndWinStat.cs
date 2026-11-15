using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.GameObjects;

namespace W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats;

public class OverallRaceAndWinStat(int mmrRange)
{
    public int MmrRange { get; set; } = mmrRange;

    public int Id => MmrRange;

    public Dictionary<string, List<MapToRaceVsRaceRatio>> PatchToStatsPerModes { get; set; } = new Dictionary<string, List<MapToRaceVsRaceRatio>>();

    public List<MapToRaceVsRaceRatio> StatsPerModes { get; set; } = new List<MapToRaceVsRaceRatio>();

    public void Apply(string map, int? mapId, string mapName, Race homeRace, Race enemyRas, bool won, string patch)
    {
        if (PatchToStatsPerModes == null)
        {
            PatchToStatsPerModes = new Dictionary<string, List<MapToRaceVsRaceRatio>>();
        }

        if (!PatchToStatsPerModes.ContainsKey(patch))
        {
            PatchToStatsPerModes[patch] = new List<MapToRaceVsRaceRatio>();
        }

        var stats = PatchToStatsPerModes[patch].SingleOrDefault(s => s.Map == map);

        if (stats == null)
        {
            // Entries written before map keys were file names have no Map and keep the key in MapName.
            // Adopting the legacy "Overall" entry keeps it first instead of appending a second one.
            stats = PatchToStatsPerModes[patch].FirstOrDefault(s => s.Map == null && s.MapName == map);
            if (stats != null)
            {
                stats.Map = map;
            }
        }

        if (stats == null)
        {
            stats = MapToRaceVsRaceRatio.Create(map);
            PatchToStatsPerModes[patch].Add(stats);
        }

        stats.UpdateMapInfo(mapId, mapName);
        stats.Ratio.RecordWin(homeRace, enemyRas, won);
    }
}
