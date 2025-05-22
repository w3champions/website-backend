using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
namespace W3ChampionsStatisticService.Friends;

[Trace]
public class FriendRequestCache(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient)
{
    private List<FriendRequest> _requests = [];
    private readonly object _lock = new();

    [NoTrace]
    public async Task<List<FriendRequest>> LoadAllFriendRequests()
    {
        await UpdateCacheIfNeeded();
        return _requests;
    }


    public async Task<List<FriendRequest>> LoadSentFriendRequests(string sender)
    {
        await UpdateCacheIfNeeded();
        return [.. _requests.Where(x => x.Sender == sender)];
    }

    public async Task<List<FriendRequest>> LoadReceivedFriendRequests(string receiver)
    {
        await UpdateCacheIfNeeded();
        return [.. _requests.Where(x => x.Receiver == receiver)];
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
            _requests = [.. _requests, req];
        }
    }

    public void Delete(FriendRequest req)
    {
        lock (_lock)
        {
            _requests.Remove(req);
        }
    }

    [NoTrace]
    private async Task UpdateCacheIfNeeded()
    {
        if (_requests.Count == 0)
        {
            var mongoCollection = CreateCollection<FriendRequest>();
            _requests = await mongoCollection.Find(r => true).ToListAsync();
        }
    }
}
