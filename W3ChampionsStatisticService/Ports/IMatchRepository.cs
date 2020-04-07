using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerStats;

namespace W3ChampionsStatisticService.Ports
{
    public interface IMatchRepository
    {
        Task<List<Matchup>> Load(int offset = 0, int pageSize = 100, int gateWay = 10);
        Task Insert(Matchup matchup);
        Task<List<Matchup>> LoadFor(string playerId, int gateWay = 10, int pageSize = 100, int offset = 0);
        Task<PlayerWinLoss> LoadPlayerWinrate(string playerId);
        Task Save(List<PlayerWinLoss> winrate);
    }

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