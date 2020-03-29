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

        public async Task Upsert(Player player)
        {
            var mongoDatabase = CreateClient();
            var mongoCollection = mongoDatabase.GetCollection<Player>(nameof(Player));
            await mongoCollection.ReplaceOneAsync(
                p => player.BattleTag == p.BattleTag,
                options: new ReplaceOptions { IsUpsert = true },
                replacement: player);
        }

        public async Task<Player> Load(string battleTag)
        {
            var mongoDatabase = CreateClient();
            var mongoCollection = mongoDatabase.GetCollection<Player>(nameof(Player));
            return (await mongoCollection.FindAsync(p => p.BattleTag == battleTag)).FirstOrDefault();
        }
    }
}