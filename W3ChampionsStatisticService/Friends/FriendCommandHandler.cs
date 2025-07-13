using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Friends;

public interface IFriendCommandHandler
{
    Task<FriendlistCache> LoadFriendList(string battleTag);
    Task CreateFriendRequest(FriendRequest request);
    Task DeleteFriendRequest(FriendRequest request);
    Task<FriendlistCache> AddFriend(FriendlistCache friendlist, string battleTag);
    Task<FriendlistCache> RemoveFriend(FriendlistCache friendlist, string battleTag);
    Task UpsertFriendList(FriendlistCache friendList);
}

[Trace]
public class FriendCommandHandler(
    IFriendService friendService
) : IFriendCommandHandler
{
    private readonly IFriendService _friendService = friendService;

    public virtual async Task<FriendlistCache> LoadFriendList(string battleTag)
    {
        return await _friendService.LoadFriendlist(battleTag);
    }

    public virtual async Task CreateFriendRequest(FriendRequest request)
    {
        await _friendService.CreateFriendRequest(request);
    }

    public virtual async Task DeleteFriendRequest(FriendRequest request)
    {
        if (request != null)
        {
            await _friendService.DeleteFriendRequest(request);
        }
    }

    public virtual async Task<FriendlistCache> AddFriend(FriendlistCache friendlist, string battleTag)
    {
        await _friendService.AddFriendship(friendlist.Id, battleTag);
        // Return updated friendlist from cache for consistency
        return await _friendService.LoadFriendlist(friendlist.Id);
    }

    public virtual async Task<FriendlistCache> RemoveFriend(FriendlistCache friendlist, string battleTag)
    {
        await _friendService.RemoveFriendship(friendlist.Id, battleTag);
        // Return updated friendlist from cache for consistency
        return await _friendService.LoadFriendlist(friendlist.Id);
    }

    public virtual async Task<bool> AddBlockedPlayer(string ownerBattleTag, string blockedBattleTag)
    {
        return await _friendService.AddBlockedPlayer(ownerBattleTag, blockedBattleTag);
    }

    public virtual async Task<bool> RemoveBlockedPlayer(string ownerBattleTag, string blockedBattleTag)
    {
        return await _friendService.RemoveBlockedPlayer(ownerBattleTag, blockedBattleTag);
    }

    public virtual async Task UpsertFriendList(FriendlistCache friendList)
    {
        await _friendService.UpsertFriendlist(friendList);
    }
}
