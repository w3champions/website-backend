using System.Collections.Generic;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats
{
    public class RaceWinRatio : Dictionary<string, Dictionary<string, Dictionary<string, WinLoss>>>
    {
        public static RaceWinRatio Create()
        {
            var ratio = new RaceWinRatio
            {
                { Race.RnD.ToString(), new Dictionary<string, Dictionary<string, WinLoss>>() },
                { Race.HU.ToString(), new Dictionary<string, Dictionary<string, WinLoss>>() },
                { Race.OC.ToString(), new Dictionary<string, Dictionary<string, WinLoss>>() },
                { Race.NE.ToString(), new Dictionary<string, Dictionary<string, WinLoss>>() },
                { Race.UD.ToString(), new Dictionary<string, Dictionary<string, WinLoss>>() },
            };
            return ratio;
        }
    }
}