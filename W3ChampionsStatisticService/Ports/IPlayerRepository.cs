using System.Threading.Tasks;
using W3ChampionsStatisticService.Players;

namespace W3ChampionsStatisticService.Ports
{
    public interface IPlayerRepository
    {
        Task Insert(Player matchups);
    }
}