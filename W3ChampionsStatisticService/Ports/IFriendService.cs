using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Friends;

namespace W3ChampionsStatisticService.Ports;

public interface IFriendService : IDisposable
{
    // Friendlist operations
    Task<FriendlistCache> LoadFriendlist(string battleTag);
    Task UpsertFriendlist(FriendlistCache friendlist);
    
    // Efficient friend operations using direct MongoDB operations
    Task<bool> AddFriendship(string player1, string player2);
    Task<bool> RemoveFriendship(string player1, string player2);
    Task<bool> AddBlockedPlayer(string ownerBattleTag, string blockedBattleTag);
    Task<bool> RemoveBlockedPlayer(string ownerBattleTag, string blockedBattleTag);
    Task<bool> SetBlockAllRequests(string battleTag, bool blockAll);
    
    // FriendRequest operations
    Task<List<FriendRequest>> LoadAllFriendRequests();
    Task<List<FriendRequest>> LoadSentFriendRequests(string sender);
    Task<List<FriendRequest>> LoadReceivedFriendRequests(string receiver);
    Task<FriendRequest> LoadFriendRequest(FriendRequest req);
    Task<FriendRequest> LoadFriendRequest(string sender, string receiver);
    Task<bool> FriendRequestExists(FriendRequest req);
    Task<FriendRequest> CreateFriendRequest(FriendRequest request);
    Task DeleteFriendRequest(FriendRequest request);
    Task RefreshCache();
}