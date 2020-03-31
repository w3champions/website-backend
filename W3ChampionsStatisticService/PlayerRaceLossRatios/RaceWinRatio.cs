using System.Collections.Generic;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PlayerRaceLossRatios
{
    public class RaceWinRatio : Dictionary<string, Dictionary<string, WinLoss>>
    {
        public static RaceWinRatio Create()
        {
            var ratio = new RaceWinRatio
            {
                { Race.HU.ToString(), new Dictionary<string, WinLoss>
                {
                    { Race.HU.ToString(), new WinLoss() },
                    { Race.OC.ToString(), new WinLoss() },
                    { Race.NE.ToString(), new WinLoss() },
                    { Race.UD.ToString(), new WinLoss() }
                }},
                { Race.OC.ToString(), new Dictionary<string, WinLoss>
                {
                    { Race.HU.ToString(), new WinLoss() },
                    { Race.OC.ToString(), new WinLoss() },
                    { Race.NE.ToString(), new WinLoss() },
                    { Race.UD.ToString(), new WinLoss() }
                }},
                { Race.NE.ToString(), new Dictionary<string, WinLoss>
                {
                    { Race.HU.ToString(), new WinLoss() },
                    { Race.OC.ToString(), new WinLoss() },
                    { Race.NE.ToString(), new WinLoss() },
                    { Race.UD.ToString(), new WinLoss() }
                }},
                { Race.UD.ToString(), new Dictionary<string, WinLoss>
                {
                    { Race.HU.ToString(), new WinLoss() },
                    { Race.OC.ToString(), new WinLoss() },
                    { Race.NE.ToString(), new WinLoss() },
                    { Race.UD.ToString(), new WinLoss() }
                }}
            };
            return ratio;
        }
    }
}