using System.Threading.Tasks;
using W3ChampionsStatisticService.Players;

namespace W3ChampionsStatisticService.Ports
{
    public interface IPlayerRepository
    {
        Task UpsertPlayer(PlayerProfile playerProfile);
        Task<PlayerProfile> Load(string battleTag);
    }
}