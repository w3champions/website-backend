using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Friends;

namespace W3ChampionsStatisticService.Ports;

public interface IFriendRepository
{
    Task<Friendlist> LoadFriendlist(string battleTag);

    /// <summary>Read-only counterpart of <see cref="LoadFriendlist"/>: never inserts a document
    /// on a cache miss, so it is safe to call for arbitrary/nonexistent battle tags.</summary>
    Task<Friendlist> LoadFriendlistOrNull(string battleTag);
    Task UpsertFriendlist(Friendlist friendlist);
    Task<FriendRequest> CreateFriendRequest(FriendRequest request);
    Task<FriendRequest> LoadFriendRequest(string sender, string receiver);
    Task<bool> FriendRequestExists(FriendRequest request);
    Task DeleteFriendRequest(FriendRequest request);
    Task<List<FriendRequest>> LoadAllFriendRequestsSentByPlayer(string sender);
    Task<List<FriendRequest>> LoadAllFriendRequestsSentToPlayer(string receiver);
}
