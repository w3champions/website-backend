using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Friends;

[Trace]
public class FriendService(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IFriendService
{
    // FriendRequest caching infrastructure
    private readonly ConcurrentDictionary<string, FriendRequest> _requestsByCompositeKey = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, FriendRequest>> _requestsBySender = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, FriendRequest>> _requestsByReceiver = new();
    private volatile bool _requestCacheInitialized = false;
    private readonly SemaphoreSlim _requestInitSemaphore = new(1, 1);

    // FriendList caching infrastructure
    private readonly ConcurrentDictionary<string, FriendlistCache> _friendListsById = new();
    private volatile bool _friendListCacheInitialized = false;
    private readonly SemaphoreSlim _friendListInitSemaphore = new(1, 1);

    private bool _disposed = false;

    private static string GetCompositeKey(string sender, string receiver) => $"{sender}|{receiver}";
    private static string GetCompositeKey(FriendRequest request) => GetCompositeKey(request.Sender, request.Receiver);

    // Generic MongoDB update helpers to reduce duplication
    private async Task<bool> AddToSetWithUpsert<TField>(string battleTag, Expression<Func<FriendlistCache, IEnumerable<TField>>> field, TField value)
    {
        var mongoCollection = CreateCollection<FriendlistCache>();
        var filter = Builders<FriendlistCache>.Filter.Eq(f => f.Id, battleTag);
        var update = Builders<FriendlistCache>.Update
            .AddToSet(field, value)
            .SetOnInsert(f => f.Id, battleTag)
            .SetOnInsert(f => f.Friends, new List<string>())
            .SetOnInsert(f => f.BlockedBattleTags, new List<string>())
            .SetOnInsert(f => f.BlockAllRequests, false);

        var result = await mongoCollection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });

        // Remove from cache to force reload on next access
        if (result.ModifiedCount > 0 || result.UpsertedId != null)
        {
            _friendListsById.TryRemove(battleTag, out _);
        }

        return result.ModifiedCount > 0 || result.UpsertedId != null;
    }

    private async Task<bool> PullFromList<TField>(string battleTag, Expression<Func<FriendlistCache, IEnumerable<TField>>> field, TField value)
    {
        var mongoCollection = CreateCollection<FriendlistCache>();
        var filter = Builders<FriendlistCache>.Filter.Eq(f => f.Id, battleTag);
        var update = Builders<FriendlistCache>.Update.Pull(field, value);

        var result = await mongoCollection.UpdateOneAsync(filter, update);

        // Remove from cache to force reload on next access
        if (result.ModifiedCount > 0)
        {
            _friendListsById.TryRemove(battleTag, out _);
        }

        return result.ModifiedCount > 0;
    }

    // Friendlist operations with efficient caching
    public async Task<FriendlistCache> LoadFriendlist(string battleTag)
    {
        await EnsureFriendListCacheInitialized();

        if (_friendListsById.TryGetValue(battleTag, out var friendlist))
        {
            return friendlist;
        }

        // TODO: This is wrong
        // Create new friendlist if not found
        friendlist = new FriendlistCache(battleTag);
        await UpsertFriendlist(friendlist);
        return friendlist;
    }

    public async Task UpsertFriendlist(FriendlistCache friendlist)
    {
        if (friendlist == null) return;

        _friendListsById.AddOrUpdate(friendlist.Id, friendlist, (_, _) => friendlist);

        try
        {
            await Upsert(friendlist, p => p.Id == friendlist.Id);
        }
        catch (Exception)
        {
            _friendListsById.TryRemove(friendlist.Id, out _);
            throw;
        }
    }

    // Internal helper methods for single-direction operations
    private async Task<bool> AddFriend(string ownerBattleTag, string friendBattleTag)
    {
        return await AddToSetWithUpsert(ownerBattleTag, f => f.Friends, friendBattleTag);
    }

    private async Task<bool> RemoveFriend(string ownerBattleTag, string friendBattleTag)
    {
        return await PullFromList(ownerBattleTag, f => f.Friends, friendBattleTag);
    }

    public async Task<bool> AddBlockedPlayer(string ownerBattleTag, string blockedBattleTag)
    {
        return await AddToSetWithUpsert(ownerBattleTag, f => f.BlockedBattleTags, blockedBattleTag);
    }

    public async Task<bool> RemoveBlockedPlayer(string ownerBattleTag, string blockedBattleTag)
    {
        return await PullFromList(ownerBattleTag, f => f.BlockedBattleTags, blockedBattleTag);
    }

    public async Task<bool> SetBlockAllRequests(string battleTag, bool blockAll)
    {
        var mongoCollection = CreateCollection<FriendlistCache>();
        var filter = Builders<FriendlistCache>.Filter.Eq(f => f.Id, battleTag);
        var update = Builders<FriendlistCache>.Update
            .Set(f => f.BlockAllRequests, blockAll)
            .SetOnInsert(f => f.Id, battleTag)
            .SetOnInsert(f => f.Friends, new List<string>())
            .SetOnInsert(f => f.BlockedBattleTags, new List<string>());

        var result = await mongoCollection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });

        // Remove from cache to force reload on next access
        if (result.ModifiedCount > 0 || result.UpsertedId != null)
        {
            _friendListsById.TryRemove(battleTag, out _);
        }

        return result.ModifiedCount > 0 || result.UpsertedId != null;
    }

    // Bidirectional friend operations (handle both directions automatically)
    public async Task<bool> AddFriendship(string player1, string player2)
    {
        if (string.Equals(player1, player2, StringComparison.OrdinalIgnoreCase))
            return false; // Cannot friend yourself

        // Execute both operations in parallel for efficiency
        var task1 = AddFriend(player1, player2);
        var task2 = AddFriend(player2, player1);

        var results = await Task.WhenAll(task1, task2);

        // Return true if either operation succeeded (handles cases where one direction already exists)
        return results[0] || results[1];
    }

    public async Task<bool> RemoveFriendship(string player1, string player2)
    {
        if (string.Equals(player1, player2, StringComparison.OrdinalIgnoreCase))
            return false; // Cannot unfriend yourself

        // Execute both operations in parallel for efficiency
        var task1 = RemoveFriend(player1, player2);
        var task2 = RemoveFriend(player2, player1);

        var results = await Task.WhenAll(task1, task2);

        // Return true if either operation succeeded
        return results[0] || results[1];
    }

    // FriendRequest operations (from Cache with database sync)
    [NoTrace]
    public virtual async Task<List<FriendRequest>> LoadAllFriendRequests()
    {
        await EnsureRequestCacheInitialized();
        return _requestsByCompositeKey.Values.ToList();
    }

    public virtual async Task<List<FriendRequest>> LoadSentFriendRequests(string sender)
    {
        await EnsureRequestCacheInitialized();
        return _requestsBySender.TryGetValue(sender, out var requests)
            ? requests.Values.ToList()
            : new List<FriendRequest>();
    }

    public virtual async Task<List<FriendRequest>> LoadReceivedFriendRequests(string receiver)
    {
        await EnsureRequestCacheInitialized();
        return _requestsByReceiver.TryGetValue(receiver, out var requests)
            ? requests.Values.ToList()
            : new List<FriendRequest>();
    }

    public virtual async Task<FriendRequest> LoadFriendRequest(FriendRequest req)
    {
        await EnsureRequestCacheInitialized();
        var key = GetCompositeKey(req);
        return _requestsByCompositeKey.TryGetValue(key, out var request) ? request : null;
    }

    public virtual async Task<FriendRequest> LoadFriendRequest(string sender, string receiver)
    {
        await EnsureRequestCacheInitialized();
        var key = GetCompositeKey(sender, receiver);
        return _requestsByCompositeKey.TryGetValue(key, out var request) ? request : null;
    }

    public virtual async Task<bool> FriendRequestExists(FriendRequest req)
    {
        await EnsureRequestCacheInitialized();
        var key = GetCompositeKey(req);
        return _requestsByCompositeKey.ContainsKey(key);
    }

    public virtual async Task<FriendRequest> CreateFriendRequest(FriendRequest request)
    {
        if (request == null) return null;

        var key = GetCompositeKey(request);

        if (_requestsByCompositeKey.TryAdd(key, request))
        {
            _requestsBySender.AddOrUpdate(request.Sender,
                new ConcurrentDictionary<string, FriendRequest> { [key] = request },
                (_, existing) => { existing.TryAdd(key, request); return existing; });

            _requestsByReceiver.AddOrUpdate(request.Receiver,
                new ConcurrentDictionary<string, FriendRequest> { [key] = request },
                (_, existing) => { existing.TryAdd(key, request); return existing; });

            try
            {
                var mongoCollection = CreateCollection<FriendRequest>();
                await mongoCollection.InsertOneAsync(request);
            }
            catch (Exception)
            {
                RemoveFromIndexes(request);
                throw;
            }
        }

        return request;
    }

    public virtual async Task DeleteFriendRequest(FriendRequest request)
    {
        if (request == null) return;

        var key = GetCompositeKey(request);

        if (_requestsByCompositeKey.TryRemove(key, out _))
        {
            RemoveFromIndexes(request);

            try
            {
                var mongoCollection = CreateCollection<FriendRequest>();
                await mongoCollection.DeleteOneAsync(r => r.Sender == request.Sender && r.Receiver == request.Receiver);
            }
            catch (Exception)
            {
                await CreateFriendRequest(request);
                throw;
            }
        }
    }

    private void RemoveFromIndexes(FriendRequest req)
    {
        var key = GetCompositeKey(req);

        if (_requestsBySender.TryGetValue(req.Sender, out var senderDict))
        {
            senderDict.TryRemove(key, out _);
            if (senderDict.IsEmpty)
            {
                _requestsBySender.TryRemove(req.Sender, out _);
            }
        }

        if (_requestsByReceiver.TryGetValue(req.Receiver, out var receiverDict))
        {
            receiverDict.TryRemove(key, out _);
            if (receiverDict.IsEmpty)
            {
                _requestsByReceiver.TryRemove(req.Receiver, out _);
            }
        }
    }

    public async Task RefreshCache()
    {
        await _requestInitSemaphore.WaitAsync();
        try
        {
            _requestsByCompositeKey.Clear();
            _requestsBySender.Clear();
            _requestsByReceiver.Clear();
            _requestCacheInitialized = false;

            await LoadRequestCacheFromDatabase();
        }
        finally
        {
            _requestInitSemaphore.Release();
        }

        await _friendListInitSemaphore.WaitAsync();
        try
        {
            _friendListsById.Clear();
            _friendListCacheInitialized = false;

            await LoadFriendListCacheFromDatabase();
        }
        finally
        {
            _friendListInitSemaphore.Release();
        }
    }

    [NoTrace]
    private async Task EnsureRequestCacheInitialized()
    {
        if (!_requestCacheInitialized)
        {
            await _requestInitSemaphore.WaitAsync();
            try
            {
                if (!_requestCacheInitialized)
                {
                    await LoadRequestCacheFromDatabase();
                }
            }
            finally
            {
                _requestInitSemaphore.Release();
            }
        }
    }

    [NoTrace]
    private async Task EnsureFriendListCacheInitialized()
    {
        if (!_friendListCacheInitialized)
        {
            await _friendListInitSemaphore.WaitAsync();
            try
            {
                if (!_friendListCacheInitialized)
                {
                    await LoadFriendListCacheFromDatabase();
                }
            }
            finally
            {
                _friendListInitSemaphore.Release();
            }
        }
    }

    private async Task LoadRequestCacheFromDatabase()
    {
        var mongoCollection = CreateCollection<FriendRequest>();
        var requests = await mongoCollection.Find(r => true).ToListAsync();

        foreach (var request in requests)
        {
            var key = GetCompositeKey(request);

            if (_requestsByCompositeKey.TryAdd(key, request))
            {
                _requestsBySender.AddOrUpdate(request.Sender,
                    new ConcurrentDictionary<string, FriendRequest> { [key] = request },
                    (_, existing) => { existing.TryAdd(key, request); return existing; });

                _requestsByReceiver.AddOrUpdate(request.Receiver,
                    new ConcurrentDictionary<string, FriendRequest> { [key] = request },
                    (_, existing) => { existing.TryAdd(key, request); return existing; });
            }
        }

        _requestCacheInitialized = true;
    }

    private async Task LoadFriendListCacheFromDatabase()
    {
        var mongoCollection = CreateCollection<FriendlistCache>();
        var friendLists = await mongoCollection.Find(r => true).ToListAsync();

        foreach (var friendList in friendLists)
        {
            _friendListsById.TryAdd(friendList.Id, friendList);
        }

        _friendListCacheInitialized = true;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _requestInitSemaphore?.Dispose();
            _friendListInitSemaphore?.Dispose();
            _disposed = true;
        }
    }
}