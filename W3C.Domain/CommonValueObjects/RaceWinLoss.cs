using W3C.Contracts.GameObjects;

namespace W3C.Domain.CommonValueObjects;

public class RaceWinLoss : WinLoss
{
    public RaceWinLoss(Race race)
    {
        Race = race;
    }

    public Race Race { get; set; }
}
