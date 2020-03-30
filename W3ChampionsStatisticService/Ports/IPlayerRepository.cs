using System.Threading.Tasks;
using W3ChampionsStatisticService.Players;

namespace W3ChampionsStatisticService.Ports
{
    public interface IPlayerRepository
    {
        Task UpsertPlayer(Player player);
        Task<Player> Load(string battleTag);
    }
}