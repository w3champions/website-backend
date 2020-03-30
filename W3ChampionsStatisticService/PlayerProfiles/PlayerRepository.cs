using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.PlayerOverviews;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class PlayerRepository : MongoDbRepositoryBase, IPlayerRepository
    {
        public PlayerRepository(DbConnctionInfo connectionInfo) : base(connectionInfo)
        {
        }

        public async Task UpsertPlayer(PlayerProfile playerProfile)
        {
            await Upsert(playerProfile, p => p.Id == playerProfile.Id);
        }

        public async Task UpsertPlayer(PlayerOverview playerOverview)
        {
            await Upsert(playerOverview, p => p.Id == playerOverview.Id);
        }

        public async Task<PlayerProfile> Load(string battleTag)
        {
            var mongoDatabase = CreateClient();
            var mongoCollection = mongoDatabase.GetCollection<PlayerProfile>(nameof(PlayerProfile));
            var elements = await mongoCollection.FindAsync(p => p.Id == battleTag);
            return elements.FirstOrDefault();
        }

        public async Task<PlayerOverview> LoadOverview(string battleTag)
        {
            var mongoDatabase = CreateClient();
            var mongoCollection = mongoDatabase.GetCollection<PlayerOverview>(nameof(PlayerOverview));
            var elements = await mongoCollection.FindAsync(p => p.Id == battleTag);
            return elements.FirstOrDefault();
        }
    }
}