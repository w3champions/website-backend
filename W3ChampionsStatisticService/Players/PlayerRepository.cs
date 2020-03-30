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

        public async Task UpsertPlayer(PlayerProfile playerProfile)
        {
            await Upsert(playerProfile, p => p.BattleTag == playerProfile.BattleTag);
        }

        public async Task<PlayerProfile> Load(string battleTag)
        {
            var mongoDatabase = CreateClient();
            var mongoCollection = mongoDatabase.GetCollection<PlayerProfile>(nameof(PlayerProfile));
            var elements = await mongoCollection.FindAsync(p => p.BattleTag == battleTag);
            return elements.FirstOrDefault();
        }
    }
}