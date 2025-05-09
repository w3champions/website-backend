using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.MatchmakingService;

namespace W3C.Domain.Repositories;

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
        // Do we really intend to allow limit to be null? And that means "give me everything"? 
        // The nullable int is a bit confusing here - assuming null means "give me everything" for now.
        return LoadAll(
            sortBy: Builders<LoadingScreenTip>.Sort.Descending(m => m.Id),
            limit: limit
        );
    }

    public async Task<LoadingScreenTip> GetRandomTip()
    {
        return await AsQueryable<LoadingScreenTip>().Sample(1).FirstOrDefaultAsync();
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
        return Upsert(loadingScreenTip, Builders<LoadingScreenTip>.Filter.Eq(n => n.Id, loadingScreenTip.Id));
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
