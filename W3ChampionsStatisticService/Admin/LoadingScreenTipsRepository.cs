using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Admin
{
    public class LoadingScreenTipsRepository : MongoDbRepositoryBase, ILoadingScreenTipsRepository
    {

        public LoadingScreenTipsRepository(MongoClient mongoClient) : base(mongoClient)
        {

        }

        public Task<List<LoadingScreenTip>> Get(int? limit = 5)
        {
            var mongoCollection = CreateCollection<LoadingScreenTip>();
            return mongoCollection
                .Find(r => true)
                .SortByDescending(m => m.Id)
                .Limit(limit).ToListAsync();
        }

        public async Task<LoadingScreenTip> GetRandomTip()
        {
            var mongoCollection = CreateCollection<LoadingScreenTip>();
            return await mongoCollection
                .AsQueryable()
                .Sample(1)
                .FirstOrDefaultAsync();
        }


        public Task Save(LoadingScreenTip loadingScreenTip)
        {
            return Insert(loadingScreenTip);
        }

        public Task DeleteTip(ObjectId objectId)
        {
            return Delete<LoadingScreenTip>(n => n.Id == objectId);
        }

        public Task UpsertTip(LoadingScreenTip loadingScreenTip)
        {
            return Upsert(loadingScreenTip, n => n.Id == loadingScreenTip.Id);
        }
    }
}
