using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats
{
    public class WinLossesPerMap
    {
        public static WinLossesPerMap Create(string mapName)
        {
            return new WinLossesPerMap
            {
                Map = mapName,
                WinLosses = new List<RaceWinLoss>
                {
                    new RaceWinLoss(Race.RnD),
                    new RaceWinLoss(Race.HU),
                    new RaceWinLoss(Race.OC),
                    new RaceWinLoss(Race.UD),
                    new RaceWinLoss(Race.NE),
                }
            };
        }
        public string Map { get; set; }
        public string MapName { get; set; }
        public List<RaceWinLoss> WinLosses { get; set; }

        public void RecordWin(Race enemyRace, in bool won)
        {
            WinLosses.Single(w => w.Race == enemyRace).RecordWin(won);
        }
    }
}