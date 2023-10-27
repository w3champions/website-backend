using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;

namespace W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

public class RaceWinRatio : List<RaceWinLossRation>
{
    public static RaceWinRatio Create()
    {
        var ratio = new RaceWinRatio
        {
            RaceWinLossRation.Create(Race.RnD),
            RaceWinLossRation.Create(Race.HU),
            RaceWinLossRation.Create(Race.OC),
            RaceWinLossRation.Create(Race.UD),
            RaceWinLossRation.Create(Race.NE)

        };
        return ratio;
    }

    public void RecordWin(Race myRace, Race enemyRace, bool won)
    {
        this.Single(r => r.Race == myRace).WinLosses.Single(r => r.Race == enemyRace).RecordWin(won);
    }
}
