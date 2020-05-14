using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.W3ChampionsStats.RaceAndWinStats
{
    public class OverallRaceAndWinStats
    {
        public int LeagueOrder { get; set; }

        public OverallRaceAndWinStats(int leagueOrder)
        {
            LeagueOrder = leagueOrder;
        }

        public string Id => nameof(OverallRaceAndWinStats);

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