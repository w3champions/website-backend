using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.MatchmakingService;

namespace W3C.Domain.Repositories
{
    public class InformationMessagesRepository : MongoDbRepositoryBase, IInformationMessagesRepository
    {
        MatchmakingServiceClient _matchmakingServiceClient;
        public InformationMessagesRepository(
            MongoClient mongoClient, 
            MatchmakingServiceClient matchmakingServiceClient) : base(mongoClient)
        {
            _matchmakingServiceClient = matchmakingServiceClient;
        }

        public Task<List<LoadingScreenTip>> GetTips(int? limit = 5)
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

        public async Task<MessageOfTheDay> GetMotd()
        {
            return await _matchmakingServiceClient.GetMotd();
        }

        public Task<HttpStatusCode> SetMotd(MessageOfTheDay motd)
        {
            return _matchmakingServiceClient.SetMotd(motd);
        }
    }
}
