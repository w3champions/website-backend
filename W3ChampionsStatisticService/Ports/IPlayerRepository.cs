using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Players;

namespace W3ChampionsStatisticService.Ports
{
    public interface IPlayerRepository
    {
        Task Upsert(Player player);
        Task<Player> Load(string battleTag);
        Task<List<Player>> LoadRange(string lastObjectId = null, int pageSize = 100);
    }
}