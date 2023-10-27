using System.Collections.Generic;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

public class RaceWinLossRation
{
    public Race Race { get; set; }
    public List<RaceWinLoss> WinLosses { get; set; }
    public static RaceWinLossRation Create(Race race)
    {
        return new RaceWinLossRation
        {
            Race = race,
            WinLosses = new List<RaceWinLoss>
            {
                new RaceWinLoss(Race.RnD),
                new RaceWinLoss(Race.HU),
                new RaceWinLoss(Race.OC),
                new RaceWinLoss(Race.UD),
                new RaceWinLoss(Race.NE)
            }
        };
    }
}
