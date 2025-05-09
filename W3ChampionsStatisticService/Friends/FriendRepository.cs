using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Friends;

public class FriendRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IFriendRepository
{
    public async Task<Friendlist> LoadFriendlist(string battleTag)
    {
        var friendlist = await LoadFirst<Friendlist>(battleTag);

        if (friendlist == null)
        {
            friendlist = new Friendlist(battleTag);
            await Insert(friendlist);
        }

        return friendlist;
    }

    public Task UpsertFriendlist(Friendlist friendlist)
    {
        return Upsert(friendlist, Builders<Friendlist>.Filter.Eq(p => p.Id, friendlist.Id));
    }

    public async Task<FriendRequest> CreateFriendRequest(FriendRequest request)
    {
        await Insert(request);
        return request;
    }

    public async Task<FriendRequest> LoadFriendRequest(string sender, string receiver)
    {
        return await LoadFirst<FriendRequest>(Builders<FriendRequest>.Filter.And(
            Builders<FriendRequest>.Filter.Eq(r => r.Sender, sender),
            Builders<FriendRequest>.Filter.Eq(r => r.Receiver, receiver)
        ));
    }

    public async Task DeleteFriendRequest(FriendRequest request)
    {
        await Delete<FriendRequest>(r => r.Sender == request.Sender && r.Receiver == request.Receiver);
    }

    public async Task<bool> FriendRequestExists(FriendRequest request)
    {
        var req = await LoadFirst(Builders<FriendRequest>.Filter.And(
            Builders<FriendRequest>.Filter.Eq(r => r.Sender, request.Sender),
            Builders<FriendRequest>.Filter.Eq(r => r.Receiver, request.Receiver)
        ));
        if (req == null) return false;
        return true;
    }

    public async Task<List<FriendRequest>> LoadAllFriendRequestsSentByPlayer(string sender)
    {
        var requests = await LoadAll(Builders<FriendRequest>.Filter.Eq(r => r.Sender, sender));
        return requests;
    }

    public async Task<List<FriendRequest>> LoadAllFriendRequestsSentToPlayer(string receiver)
    {
        var requests = await LoadAll(Builders<FriendRequest>.Filter.Eq(r => r.Receiver, receiver));
        return requests;
    }
}
