using W3C.Domain.CommonValueObjects;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroWinrate;

public class HeroWinRate
{
    public string HeroCombo { get; set; }
    public WinLoss WinLoss { get; set; } = new WinLoss();

    public static HeroWinRate Create(string heroCombo)
    {
        return new HeroWinRate
        {
            HeroCombo = heroCombo
        };
    }

    public void RecordGame(bool won)
    {
        WinLoss.RecordWin(won);
    }
}
