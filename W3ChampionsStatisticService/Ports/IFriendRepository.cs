using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Friends;

namespace W3ChampionsStatisticService.Ports;

public interface IFriendRepository
{
    Task<Friendlist> LoadFriendlist(string battleTag);
    Task UpsertFriendlist(Friendlist friendlist);
    Task<FriendRequest> CreateFriendRequest(FriendRequest request);
    Task<FriendRequest> LoadFriendRequest(string sender, string receiver);
    Task<bool> FriendRequestExists(FriendRequest request);
    Task DeleteFriendRequest(FriendRequest request);
    Task<List<FriendRequest>> LoadAllFriendRequestsSentByPlayer(string sender);
    Task<List<FriendRequest>> LoadAllFriendRequestsSentToPlayer(string receiver);
}
