using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Friends;

public interface IFriendRequestCache
{
    Task<List<FriendRequest>> LoadAllFriendRequests();
    Task<List<FriendRequest>> LoadSentFriendRequests(string sender);
    Task<List<FriendRequest>> LoadReceivedFriendRequests(string receiver);
    Task<FriendRequest> LoadFriendRequest(FriendRequest req);
    Task<bool> FriendRequestExists(FriendRequest req);
    void Insert(FriendRequest req);
    void Delete(FriendRequest req);
    void MarkReceivedSeen(string receiver, DateTime seenAt);
}

[Trace]
public class FriendRequestCache(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IFriendRequestCache
{
    private List<FriendRequest> _requests = [];
    private readonly object _lock = new();

    [NoTrace]
    public virtual async Task<List<FriendRequest>> LoadAllFriendRequests()
    {
        await UpdateCacheIfNeeded();
        return _requests;
    }

    public virtual async Task<List<FriendRequest>> LoadSentFriendRequests(string sender)
    {
        await UpdateCacheIfNeeded();
        return [.. _requests.Where(x => x.Sender == sender)];
    }

    public virtual async Task<List<FriendRequest>> LoadReceivedFriendRequests(string receiver)
    {
        await UpdateCacheIfNeeded();
        return [.. _requests.Where(x => x.Receiver == receiver)];
    }

    public virtual async Task<FriendRequest> LoadFriendRequest(FriendRequest req)
    {
        await UpdateCacheIfNeeded();
        return _requests.FirstOrDefault(x => x.Sender == req.Sender && x.Receiver == req.Receiver);
    }

    public virtual async Task<bool> FriendRequestExists(FriendRequest req)
    {
        await UpdateCacheIfNeeded();
        return _requests.FirstOrDefault(x => x.Sender == req.Sender && x.Receiver == req.Receiver) != null;
    }

    public virtual void Insert(FriendRequest req)
    {
        lock (_lock)
        {
            _requests = [.. _requests, req];
        }
    }

    public virtual void Delete(FriendRequest request)
    {
        lock (_lock)
        {
            _requests.RemoveAll(r => r.Sender == request.Sender && r.Receiver == request.Receiver);
        }
    }

    public virtual void MarkReceivedSeen(string receiver, DateTime seenAt)
    {
        lock (_lock)
        {
            foreach (var request in _requests.Where(r => r.Receiver == receiver && r.SeenAt == null))
            {
                request.SeenAt = seenAt;
            }
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
