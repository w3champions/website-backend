using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Repositories;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Friends;

[Trace]
public class FriendListCache(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient)
{
    private List<Friendlist> _friendLists = [];
    private readonly object _lock = new();

    public async Task<Friendlist> LoadFriendList(string battleTag)
    {
        await UpdateCacheIfNeeded();
        return _friendLists.FirstOrDefault(x => x.Id == battleTag);
    }

    public void Upsert(Friendlist friendList)
    {
        lock (_lock)
        {
            _friendLists = [.. _friendLists.Where(x => x.Id != friendList.Id), friendList];
        }

    }

    public void Delete(Friendlist friendList)
    {
        lock (_lock)
        {
            _friendLists.Remove(friendList);
        }
    }

    private async Task UpdateCacheIfNeeded()
    {
        if (_friendLists.Count == 0)
        {
            var mongoCollection = CreateCollection<Friendlist>();
            _friendLists = await mongoCollection.Find(r => true).ToListAsync();
        }
    }
}
