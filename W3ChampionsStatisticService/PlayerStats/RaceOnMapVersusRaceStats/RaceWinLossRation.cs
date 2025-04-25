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
            WinLosses =
            [
                new(Race.RnD),
                new(Race.HU),
                new(Race.OC),
                new(Race.UD),
                new(Race.NE)
            ]
        };
    }
}
