using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Ladder
{
    public class PlayerWinLoss : IIdentifiable
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