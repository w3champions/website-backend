using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Friends;

public class FriendRequestCache(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient)
{
    private List<FriendRequest> _requests = new List<FriendRequest>();
    private Object _lock = new Object();

    public async Task<List<FriendRequest>> LoadAllFriendRequests()
    {
        await UpdateCacheIfNeeded();
        return _requests;
    }

    public async Task<List<FriendRequest>> LoadSentFriendRequests(string sender)
    {
        await UpdateCacheIfNeeded();
        return _requests.Where(x => x.Sender == sender).ToList();
    }

    public async Task<List<FriendRequest>> LoadReceivedFriendRequests(string receiver)
    {
        await UpdateCacheIfNeeded();
        return _requests.Where(x => x.Receiver == receiver).ToList();
    }

    public async Task<FriendRequest> LoadFriendRequest(FriendRequest req)
    {
        await UpdateCacheIfNeeded();
        return _requests.SingleOrDefault(x => x.Sender == req.Sender && x.Receiver == req.Receiver);
    }

    public async Task<bool> FriendRequestExists(FriendRequest req)
    {
        await UpdateCacheIfNeeded();
        return _requests.SingleOrDefault(x => x.Sender == req.Sender && x.Receiver == req.Receiver) != null;
    }

    public void Insert(FriendRequest req)
    {
        lock (_lock)
        {
            _requests = _requests.Append(req).ToList();
        }
    }

    public void Delete(FriendRequest req)
    {
        lock (_lock)
        {
            _requests.Remove(req);
        }
    }

    private async Task UpdateCacheIfNeeded()
    {
        if (_requests.Count == 0)
        {
            var mongoCollection = CreateCollection<FriendRequest>();
            _requests = await mongoCollection.Find(r => true).ToListAsync();
        }
    }
}
