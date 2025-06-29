using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Friends;

public interface IFriendCommandHandler
{
    Task<Friendlist> LoadFriendList(string battleTag);
    Task CreateFriendRequest(FriendRequest request);
    Task DeleteFriendRequest(FriendRequest request);
    Task<Friendlist> AddFriend(Friendlist friendlist, string battleTag);
    Task<Friendlist> RemoveFriend(Friendlist friendlist, string battleTag);
    Task UpsertFriendList(Friendlist friendList);
}

[Trace]
public class FriendCommandHandler(
    FriendRepository friendRepository,
    FriendRequestCache friendRequestCache,
    FriendListCache friendListCache
) : IFriendCommandHandler
{
    private readonly FriendRepository _friendRepository = friendRepository;
    private readonly FriendRequestCache _friendRequestCache = friendRequestCache;
    private readonly FriendListCache _friendListCache = friendListCache;

    public virtual async Task<Friendlist> LoadFriendList(string battleTag)
    {
        var friendList = await _friendListCache.LoadFriendList(battleTag);
        if (friendList == null)
        {
            friendList = new Friendlist(battleTag);
            await UpsertFriendList(friendList);
        }
        return friendList;
    }

    public virtual async Task CreateFriendRequest(FriendRequest request)
    {
        await _friendRepository.CreateFriendRequest(request);
        _friendRequestCache.Insert(request);
    }

    public virtual async Task DeleteFriendRequest(FriendRequest request)
    {
        if (request != null)
        {
            await _friendRepository.DeleteFriendRequest(request);
            _friendRequestCache.Delete(request);
        }
    }

    public virtual async Task<Friendlist> AddFriend(Friendlist friendlist, string battleTag)
    {
        if (!friendlist.Friends.Contains(battleTag))
        {
            friendlist.Friends.Add(battleTag);
        }
        await UpsertFriendList(friendlist);
        return friendlist;
    }

    public virtual async Task<Friendlist> RemoveFriend(Friendlist friendlist, string battleTag)
    {
        var friend = friendlist.Friends.SingleOrDefault(bTag => bTag == battleTag);
        if (friend != null)
        {
            friendlist.Friends.Remove(friend);
        }
        await UpsertFriendList(friendlist);
        return friendlist;
    }

    public virtual async Task UpsertFriendList(Friendlist friendList)
    {
        await _friendRepository.UpsertFriendlist(friendList);
        _friendListCache.Upsert(friendList);
    }
}
