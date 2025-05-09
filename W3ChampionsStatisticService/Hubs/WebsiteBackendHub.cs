using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using W3ChampionsStatisticService.Friends;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Hubs;

public class WebsiteBackendHub(
    IW3CAuthenticationService authenticationService,
    ConnectionMapping connections,
    IHttpContextAccessor contextAccessor,
    FriendRepository friendRepository,
    FriendRequestCache friendRequestCache,
    IPersonalSettingsRepository personalSettingsRepository
) : Hub
{
    private readonly IW3CAuthenticationService _authenticationService = authenticationService;
    private readonly ConnectionMapping _connections = connections;
    private readonly IHttpContextAccessor _contextAccessor = contextAccessor;
    private readonly FriendRepository _friendRepository = friendRepository;
    private readonly FriendRequestCache _friendRequestCache = friendRequestCache;
    private readonly IPersonalSettingsRepository _personalSettingsRepository = personalSettingsRepository;

    public override async Task OnConnectedAsync()
    {
        var accessToken = _contextAccessor?.HttpContext?.Request.Query["access_token"];
        W3CUserAuthenticationDto w3cUserAuthentication = _authenticationService.GetUserByToken(accessToken, false);
        if (w3cUserAuthentication == null)
        {
            await Clients.Caller.SendAsync("AuthorizationFailed");
            Context.Abort();
            return;
        }
        WebSocketUser user = new() { BattleTag = w3cUserAuthentication.BattleTag, ConnectionId = Context.ConnectionId };
        await LoginAsAuthenticated(user);
        await NotifyFriendsWithIsOnline(user.BattleTag, true);
        await base.OnConnectedAsync();
    }

    internal async Task LoginAsAuthenticated(WebSocketUser user)
    {
        _connections.Add(Context.ConnectionId, user);
        await Clients.Caller.SendAsync(WebsiteBackendSocketResponseType.Connected.ToString());
    }

    public async Task LoadFriendListAndRequests()
    {
        var currentUser = _connections.GetUser(Context.ConnectionId)?.BattleTag;
        if (currentUser == null)
            return;
        Friendlist friendList = await _friendRepository.LoadFriendlist(currentUser);
        List<FriendRequest> sentRequests = await _friendRequestCache.LoadSentFriendRequests(currentUser);
        List<FriendRequest> receivedRequests = await _friendRequestCache.LoadReceivedFriendRequests(currentUser);
        await Clients.Caller.SendAsync(FriendResponseType.FriendResponseData.ToString(), friendList, sentRequests, receivedRequests);
    }

    public async Task LoadFriendsWithPictures()
    {
        var currentUser = _connections.GetUser(Context.ConnectionId)?.BattleTag;
        if (currentUser == null)
            return;
        List<FriendUser> friends = await GetFriends(currentUser);
        await Clients.Caller.SendAsync(FriendResponseType.FriendsWithPictures.ToString(), friends);
    }

    public async Task MakeFriendRequest(FriendRequest req)
    {
        try
        {
            PersonalSetting personalSetting =
                await _personalSettingsRepository.Find(req.Receiver) ?? throw new ValidationException($"Player {req.Receiver} not found.");

            if (req.Sender.Equals(req.Receiver, StringComparison.CurrentCultureIgnoreCase))
            {
                throw new ValidationException("Cannot request yourself as a friend.");
            }
            var sentRequests = await _friendRequestCache.LoadSentFriendRequests(req.Sender);
            if (sentRequests.Count > 10)
            {
                throw new ValidationException("You have too many pending friend requests.");
            }
            var receiverFriendlist = await _friendRepository.LoadFriendlist(req.Receiver);
            await CanMakeFriendRequest(receiverFriendlist, req);
            await _friendRepository.CreateFriendRequest(req);
            await _friendRequestCache.Insert(req);
            sentRequests.Add(req);
            await Clients.Caller.SendAsync(
                FriendResponseType.FriendResponseData.ToString(),
                null,
                sentRequests,
                null,
                $"Friend request sent to {req.Receiver}!"
            );

            var requestsReceivedByOtherPlayer = await _friendRequestCache.LoadReceivedFriendRequests(req.Receiver);
            await PushFriendResponseDataToPlayer(req.Receiver, null, null, requestsReceivedByOtherPlayer);
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync(FriendResponseType.FriendResponseMessage.ToString(), ex.Message);
        }
    }

    public async Task DeleteOutgoingFriendRequest(FriendRequest req)
    {
        try
        {
            var request = await _friendRequestCache.LoadFriendRequest(req) ?? throw new ValidationException("Could not find a friend request to delete.");
            await _friendRepository.DeleteFriendRequest(request);
            await _friendRequestCache.Delete(request);

            List<FriendRequest> sentRequests = await _friendRequestCache.LoadSentFriendRequests(req.Sender);
            await Clients.Caller.SendAsync(
                FriendResponseType.FriendResponseData.ToString(),
                null,
                sentRequests,
                null,
                $"Friend request to {req.Receiver} deleted!"
            );

            var requestsReceivedByOtherPlayer = await _friendRequestCache.LoadReceivedFriendRequests(req.Receiver);
            await PushFriendResponseDataToPlayer(req.Receiver, null, null, requestsReceivedByOtherPlayer);
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync(FriendResponseType.FriendResponseMessage.ToString(), ex.Message);
        }
    }

    public async Task AcceptIncomingFriendRequest(FriendRequest req)
    {
        try
        {
            var currentUserFriendlist = await _friendRepository.LoadFriendlist(req.Receiver);
            var senderFriendlist = await _friendRepository.LoadFriendlist(req.Sender);

            var request = await _friendRequestCache.LoadFriendRequest(req) ?? throw new ValidationException("Could not find a friend request to accept.");
            await _friendRepository.DeleteFriendRequest(request);
            await _friendRequestCache.Delete(request);
            var reciprocalRequest = await _friendRequestCache.LoadFriendRequest(new FriendRequest(req.Receiver, req.Sender));
            if (reciprocalRequest != null)
            {
                await _friendRepository.DeleteFriendRequest(reciprocalRequest);
                await _friendRequestCache.Delete(reciprocalRequest);
            }

            if (!currentUserFriendlist.Friends.Contains(req.Sender))
            {
                currentUserFriendlist.Friends.Add(req.Sender);
            }
            await _friendRepository.UpsertFriendlist(currentUserFriendlist);

            if (!senderFriendlist.Friends.Contains(req.Receiver))
            {
                senderFriendlist.Friends.Add(req.Receiver);
            }
            await _friendRepository.UpsertFriendlist(senderFriendlist);

            List<FriendRequest> sentRequests = await _friendRequestCache.LoadSentFriendRequests(req.Receiver);
            List<FriendRequest> receivedRequests = await _friendRequestCache.LoadReceivedFriendRequests(req.Receiver);
            await Clients.Caller.SendAsync(
                FriendResponseType.FriendResponseData.ToString(),
                currentUserFriendlist,
                sentRequests,
                receivedRequests,
                $"Friend request from {req.Sender} accepted!"
            );

            List<FriendUser> receiverFriends = await GetFriends(req.Receiver);
            await Clients.Caller.SendAsync(FriendResponseType.FriendsWithPictures.ToString(), receiverFriends);

            await PushFriendsWithPicturesToPlayer(req.Sender);
            var requestsSentByOtherPlayer = await _friendRequestCache.LoadSentFriendRequests(req.Sender);
            await PushFriendResponseDataToPlayer(req.Sender, senderFriendlist, requestsSentByOtherPlayer);
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync(FriendResponseType.FriendResponseMessage.ToString(), ex.Message);
        }
    }

    public async Task DenyIncomingFriendRequest(FriendRequest req)
    {
        try
        {
            var request = await _friendRequestCache.LoadFriendRequest(req) ?? throw new ValidationException("Could not find a friend request to deny.");
            await _friendRepository.DeleteFriendRequest(request);
            await _friendRequestCache.Delete(request);

            List<FriendRequest> receivedRequests = await _friendRequestCache.LoadReceivedFriendRequests(req.Receiver);
            await Clients.Caller.SendAsync(
                FriendResponseType.FriendResponseData.ToString(),
                null,
                null,
                receivedRequests,
                $"Friend request from {req.Sender} denied!"
            );

            var sentRequests = await _friendRequestCache.LoadSentFriendRequests(req.Sender);
            await PushFriendResponseDataToPlayer(req.Sender, null, sentRequests);
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync(FriendResponseType.FriendResponseMessage.ToString(), ex.Message);
        }
    }

    public async Task BlockIncomingFriendRequest(FriendRequest req)
    {
        try
        {
            var currentUserFriendlist = await _friendRepository.LoadFriendlist(req.Receiver);
            CanBlock(currentUserFriendlist, req.Sender);

            var request = await _friendRequestCache.LoadFriendRequest(req) ?? throw new ValidationException("Could not find a friend request to block.");
            await _friendRepository.DeleteFriendRequest(request);
            await _friendRequestCache.Delete(request);

            currentUserFriendlist.BlockedBattleTags.Add(req.Sender);
            await _friendRepository.UpsertFriendlist(currentUserFriendlist);

            List<FriendRequest> receivedRequests = await _friendRequestCache.LoadReceivedFriendRequests(req.Receiver);
            await Clients.Caller.SendAsync(
                FriendResponseType.FriendResponseData.ToString(),
                currentUserFriendlist,
                null,
                receivedRequests,
                $"Friend requests from {req.Sender} blocked!"
            );

            var sentRequests = await _friendRequestCache.LoadSentFriendRequests(req.Sender);
            await PushFriendResponseDataToPlayer(req.Sender, null, sentRequests);
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync(FriendResponseType.FriendResponseMessage.ToString(), ex.Message);
        }
    }

    public async Task UnblockFriendRequestsFromPlayer(string battleTag)
    {
        var currentUser = _connections.GetUser(Context.ConnectionId)?.BattleTag;
        if (currentUser == null)
            return;
        try
        {
            var friendList = await _friendRepository.LoadFriendlist(currentUser);

            var itemToRemove =
                friendList.BlockedBattleTags.SingleOrDefault(bTag => bTag == battleTag) ?? throw new ValidationException("Could not find a player to unblock.");
            friendList.BlockedBattleTags.Remove(itemToRemove);
            await _friendRepository.UpsertFriendlist(friendList);

            await Clients.Caller.SendAsync(
                FriendResponseType.FriendResponseData.ToString(),
                friendList,
                null,
                null,
                $"Friend requests from {battleTag} unblocked!"
            );
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync(FriendResponseType.FriendResponseMessage.ToString(), ex.Message);
        }
    }

    public async Task RemoveFriend(string friend)
    {
        var currentUser = _connections.GetUser(Context.ConnectionId)?.BattleTag;
        if (currentUser == null)
            return;
        try
        {
            var currentUserFriendlist = await _friendRepository.LoadFriendlist(currentUser);
            var friendRelation1 = currentUserFriendlist.Friends.SingleOrDefault(bTag => bTag == friend);
            if (friendRelation1 != null)
            {
                currentUserFriendlist.Friends.Remove(friendRelation1);
            }
            await _friendRepository.UpsertFriendlist(currentUserFriendlist);

            var otherUserFriendlist = await _friendRepository.LoadFriendlist(friend);
            var friendRelation2 = otherUserFriendlist.Friends.SingleOrDefault(bTag => bTag == currentUser);
            if (friendRelation2 != null)
            {
                otherUserFriendlist.Friends.Remove(friendRelation2);
            }
            await _friendRepository.UpsertFriendlist(otherUserFriendlist);

            await Clients.Caller.SendAsync(
                FriendResponseType.FriendResponseData.ToString(),
                currentUserFriendlist,
                null,
                null,
                $"Removed {friend} from friends."
            );

            List<FriendUser> currentUserFriends = await GetFriends(currentUser);
            await Clients.Caller.SendAsync(FriendResponseType.FriendsWithPictures.ToString(), currentUserFriends);

            await PushFriendsWithPicturesToPlayer(friend);

            await PushFriendResponseDataToPlayer(friend, otherUserFriendlist);
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync(FriendResponseType.FriendResponseMessage.ToString(), ex.Message);
        }
    }

    private async Task<List<FriendUser>> GetFriends(string battleTag)
    {
        Friendlist friendList = await _friendRepository.LoadFriendlist(battleTag);
        if (friendList.Friends.Count == 0)
            return [];

        // TODO: Uncomment after the launcher has been updated to avoid querying the db excessively after a website-backend restart/deploy.
        // List<PersonalSetting> personalSettings = await _personalSettingsRepository.LoadMany(friendList.Friends.ToArray());
        // List<FriendUser> friends = personalSettings.Select(x => new FriendUser
        // {
        //     BattleTag = x.Id,
        //     ProfilePicture = x.ProfilePicture
        // }).ToList();

        var friendStatus = _connections.GetUsersOnlineStatus(friendList.Friends);
        List<FriendUser> friends = friendStatus
            .Select(friend => new FriendUser
            {
                BattleTag = friend.Key,
                ProfilePicture = ProfilePicture.Default(),
                IsOnline = friend.Value,
            })
            .ToList();

        return friends ?? [];
    }

    private async Task PushFriendResponseDataToPlayer(
        string battleTag,
        Friendlist friendList = null,
        List<FriendRequest> sentRequests = null,
        List<FriendRequest> receivedRequests = null,
        string message = null
    )
    {
        var player = _connections.GetUsers().FirstOrDefault(x => x.BattleTag == battleTag);
        if (player?.ConnectionId == null)
            return;
        await Clients
            .Client(player.ConnectionId)
            .SendAsync(FriendResponseType.FriendResponseData.ToString(), friendList, sentRequests, receivedRequests, message);
    }

    private async Task PushFriendsWithPicturesToPlayer(string battleTag)
    {
        var player = _connections.GetUsers().FirstOrDefault(x => x.BattleTag == battleTag);
        if (player?.ConnectionId == null)
            return;
        List<FriendUser> friends = await GetFriends(battleTag);
        await Clients.Client(player.ConnectionId).SendAsync(FriendResponseType.FriendsWithPictures.ToString(), friends);
    }

    private async Task CanMakeFriendRequest(Friendlist friendList, FriendRequest req)
    {
        if (friendList.BlockAllRequests || friendList.BlockedBattleTags.Contains(req.Sender))
        {
            throw new ValidationException("This player is not accepting friend requests.");
        }
        if (friendList.Friends.Contains(req.Sender))
        {
            throw new ValidationException("You are already friends with this player.");
        }
        var requestAlreadyExists = await _friendRequestCache.FriendRequestExists(req);
        if (requestAlreadyExists)
        {
            throw new ValidationException("You have already requested to be friends with this player.");
        }
    }

    private static void CanBlock(Friendlist friendList, string battleTag)
    {
        if (friendList.BlockedBattleTags.Contains(battleTag))
        {
            throw new ValidationException("You have already blocked this player.");
        }
        if (friendList.Friends.Contains(battleTag))
        {
            throw new ValidationException("You cannot block a player you are friends with.");
        }
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        var user = _connections.GetUser(Context.ConnectionId);
        if (user != null)
        {
            _connections.Remove(Context.ConnectionId);
        }

        await NotifyFriendsWithIsOnline(user.BattleTag, false);
        await base.OnDisconnectedAsync(exception);
    }

    private async Task NotifyFriendsWithIsOnline(string battleTag, bool isOnline)
    {
        var friendList = await _friendRepository.LoadFriendlist(battleTag);
        var onlineFriends = friendList
            .Friends.Where(tag => _connections.IsUserOnline(tag))
            .Select(tag => _connections.GetConnectionId(tag))
            .SelectMany(connection => connection);

        await Clients.Clients(onlineFriends).SendAsync(FriendResponseType.FriendOnlineStatus.ToString(), battleTag, isOnline);
    }
}
