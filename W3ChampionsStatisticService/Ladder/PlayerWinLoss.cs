using W3ChampionsStatisticService.PlayerStats;

namespace W3ChampionsStatisticService.Ladder
{
    public class PlayerWinLoss
    {
        public string Id { get; set; }
        public WinLoss Stats { get; set; } = new WinLoss();

        public PlayerWinLoss Apply(in bool won)
        {
            Stats.RecordWin(won);
            return this;
        }

        public static PlayerWinLoss Create(string battleTag)
        {
            return new PlayerWinLoss
            {
                Id = battleTag
            };
        }
    }
}