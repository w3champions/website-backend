using W3C.Contracts.GameObjects;

namespace W3C.Domain.CommonValueObjects;

public class RaceWinLoss(Race race) : WinLoss
{
    public Race Race { get; set; } = race;
}
