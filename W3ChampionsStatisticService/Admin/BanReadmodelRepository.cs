using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.PadEvents.PadSync;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Admin
{
    public class BanReadmodelRepository : MongoDbRepositoryBase
    {
        public BanReadmodelRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public Task<BannedPlayer> GetBan(string battleTag)
        {
            return LoadFirst<BannedPlayer>(battleTag);
        }

        public Task UpdateBans(List<BannedPlayer> bans)
        {
            return UpsertMany(bans);
        }

        public Task<List<BannedPlayer>> GetBans()
        {
            var mongoCollection = CreateCollection<BannedPlayer>();
            return mongoCollection.Find(s => true).SortByDescending(s => s.endDate).ToListAsync();
        }
    }
}