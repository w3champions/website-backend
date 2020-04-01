using System.Threading.Tasks;
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

        public Task<PlayerProfile> Load(string battleTag)
        {
            return LoadFirst<PlayerProfile>(p => p.Id == battleTag);
        }

        public Task<PlayerOverview> LoadOverview(string battleTag)
        {
            return LoadFirst<PlayerOverview>(p => p.Id == battleTag);
        }
    }
}