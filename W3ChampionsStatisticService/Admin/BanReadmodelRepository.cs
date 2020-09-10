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

        public Task<BannedPlayerReadmodel> GetBan(string battleTag)
        {
            return LoadFirst<BannedPlayerReadmodel>(battleTag);
        }

        public Task UpdateBans(List<BannedPlayerReadmodel> bans)
        {
            return UpsertMany(bans);
        }

        public Task<List<BannedPlayerReadmodel>> GetBans()
        {
            var mongoCollection = CreateCollection<BannedPlayerReadmodel>();
            return mongoCollection.Find(s => true).SortByDescending(s => s.endDate).ToListAsync();
        }
    }
}