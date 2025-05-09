using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Friends;

public class FriendRequestCache(MongoClient mongoClient, ITransactionCoordinator transactionCoordinator)
    : MongoDbRepositoryBase(mongoClient, transactionCoordinator)
{
    private readonly ITransactionCoordinator _transactionCoordinator = transactionCoordinator;
    private List<FriendRequest> _requests = [];
    private readonly object _lock = new();

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

    public async Task Insert(FriendRequest req)
    {
        await _transactionCoordinator.RegisterOnSuccessHandler(() =>
        {
            lock (_lock)
            {
                _requests = [.. _requests, req];
            }
            return Task.CompletedTask;
        });
    }

    public async Task Delete(FriendRequest req)
    {
        await _transactionCoordinator.RegisterOnSuccessHandler(() =>
        {
            lock (_lock)
            {
                _requests.Remove(req);
            }
            return Task.CompletedTask;
        });
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
