using W3C.Domain.CommonValueObjects;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Ladder
{
    public class PlayerWinLoss : IIdentifiable
    {
        public string Id { get; set; }
        public WinLoss Stats { get; set; } = new WinLoss();
        public int Season { get; set; }

        public PlayerWinLoss Apply(in bool won)
        {
            Stats.RecordWin(won);
            return this;
        }

        public static PlayerWinLoss Create(string battleTag, int season)
        {
            return new PlayerWinLoss
            {
                Id = $"{season}_{battleTag}",
                Season = season
            };
        }
    }
}