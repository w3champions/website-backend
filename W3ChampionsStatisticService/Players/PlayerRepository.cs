using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Players
{
    public class PlayerRepository : MongoDbRepositoryBase, IPlayerRepository
    {
        public PlayerRepository(DbConnctionInfo connctionInfo) : base(connctionInfo)
        {
        }

        public async Task UpsertPlayer(Player player)
        {
            await Upsert(player, p => p.BattleTag == player.BattleTag);
        }

        public async Task<Player> Load(string battleTag)
        {
            var mongoDatabase = CreateClient();
            var mongoCollection = mongoDatabase.GetCollection<Player>(nameof(Player));
            var elements = await mongoCollection.FindAsync(p => p.BattleTag == battleTag);
            return elements.FirstOrDefault();
        }
    }
}