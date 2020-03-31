using System.Collections.Generic;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerRaceLossRatios;

namespace W3ChampionsStatisticService.PlayerMapAndRaceRatios
{
    public class RaceWinAndMapRatio : Dictionary<string, Dictionary<string, Dictionary<string, WinLoss>>>
    {
        public static RaceWinAndMapRatio Create()
        {
            var ratio = new RaceWinAndMapRatio
            {
                { Race.HU.ToString(), new Dictionary<string, Dictionary<string, WinLoss>>() },
                { Race.OC.ToString(), new Dictionary<string, Dictionary<string, WinLoss>>() },
                { Race.NE.ToString(), new Dictionary<string, Dictionary<string, WinLoss>>() },
                { Race.UD.ToString(), new Dictionary<string, Dictionary<string, WinLoss>>() },
            };
            return ratio;
        }
    }
}