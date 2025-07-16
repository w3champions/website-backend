using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using W3ChampionsStatisticService.Friends;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Services;
using static W3ChampionsStatisticService.Filters.SignalRTraceContextFilter;
using Serilog;

namespace W3ChampionsStatisticService.Hubs;

[Trace]
public class WebsiteBackendHub(
    IW3CAuthenticationService authenticationService,
    ConnectionMapping connections,
    IHttpContextAccessor contextAccessor,
    IFriendService friendService,
    IPersonalSettingsRepository personalSettingsRepository,
    TracingService tracingService
) : Hub
{
    static WebsiteBackendHub()
    {
        // Check if any of the public handlers have no arguments, as we need them to have at least one argument due to tracing requirements.
        var methods = typeof(WebsiteBackendHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var allowedZeroArgMethods = new HashSet<string> { "LoadFriendListAndRequests", "LoadFriendsWithPictures" };
        foreach (var methodInfo in methods)
        {
            if (methodInfo.IsSpecialName) continue;

            if (methodInfo.GetBaseDefinition() == methodInfo && methodInfo.GetParameters().Length == 0)
            {
                if (allowedZeroArgMethods.Contains(methodInfo.Name)) continue;
                throw new InvalidOperationException($"Hub method '{methodInfo.Name}' in {nameof(WebsiteBackendHub)} must have at least one parameter due to tracing requirements. " +
                                                    $"Please add a '{nameof(PreventZeroArgumentHandler)}' parameter or ensure it has other arguments.");
            }
        }
    }

    private readonly IW3CAuthenticationService _authenticationService = authenticationService;
    private readonly ConnectionMapping _connections = connections;
    private readonly IHttpContextAccessor _contextAccessor = contextAccessor;
    private readonly IFriendService _friendService = friendService;
    private readonly IPersonalSettingsRepository _personalSettingsRepository = personalSettingsRepository;
    private readonly TracingService _tracingService = tracingService;


    [NoTrace]
    public override async Task OnConnectedAsync()
    {
        await _tracingService.ExecuteWithSpanAsync(this, async () =>
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
        }, forceNewRoot: true);
        await base.OnConnectedAsync();
    }

    internal async Task LoginAsAuthenticated(WebSocketUser user)
    {
        _connections.Add(Context.ConnectionId, user);
        await Clients.Caller.SendAsync(WebsiteBackendSocketResponseType.Connected.ToString());
    }

    // Required for backwards compatibility
    public async Task LoadFriendListAndRequests()
    {
        await this.LoadFriendListAndRequestsTraced(new PreventZeroArgumentHandler());
    }

    public async Task LoadFriendListAndRequestsTraced(PreventZeroArgumentHandler _)
    {
        var currentUser = _connections.GetUser(Context.ConnectionId)?.BattleTag;
        if (currentUser == null)
        {
            return;
        }
        FriendlistCache friendList = await _friendService.LoadFriendlist(currentUser);
        List<FriendRequest> sentRequests = await _friendService.LoadSentFriendRequests(currentUser);
        List<FriendRequest> receivedRequests = await _friendService.LoadReceivedFriendRequests(currentUser);
        await Clients.Caller.SendAsync(FriendResponseType.FriendResponseData.ToString(), friendList, sentRequests, receivedRequests);
    }

    // Required for backwards compatibility
    public async Task LoadFriendsWithPictures()
    {
        await this.LoadFriendsWithPicturesTraced(new PreventZeroArgumentHandler());
    }

    public async Task LoadFriendsWithPicturesTraced(PreventZeroArgumentHandler _)
    {
        var currentUser = _connections.GetUser(Context.ConnectionId)?.BattleTag;
        if (currentUser == null)
        {
            return;
        }
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
            var sentRequests = await _friendService.LoadSentFriendRequests(req.Sender);
            if (sentRequests.Count > 10)
            {
                throw new ValidationException("You have too many pending friend requests.");
            }

            var receiverFriendlist = await _friendService.LoadFriendlist(req.Receiver);
            await CanMakeFriendRequest(receiverFriendlist, req);
            await _friendService.CreateFriendRequest(req);
            sentRequests.Add(req);

            await Clients.Caller.SendAsync(
                FriendResponseType.FriendResponseData.ToString(),
                null,
                sentRequests,
                null,
                $"Friend request sent to {req.Receiver}!"
            );

            var requestsReceivedByOtherPlayer = await _friendService.LoadReceivedFriendRequests(req.Receiver);
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
            var request = await _friendService.LoadFriendRequest(req) ?? throw new ValidationException("Could not find a friend request to delete.");
            await _friendService.DeleteFriendRequest(request);

            List<FriendRequest> sentRequests = await _friendService.LoadSentFriendRequests(req.Sender);
            await Clients.Caller.SendAsync(
                FriendResponseType.FriendResponseData.ToString(),
                null,
                sentRequests,
                null,
                $"Friend request to {req.Receiver} deleted!"
            );

            var requestsReceivedByOtherPlayer = await _friendService.LoadReceivedFriendRequests(req.Receiver);
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
            var request = await _friendService.LoadFriendRequest(req) ?? throw new ValidationException("Could not find a friend request to accept.");
            var reciprocalRequest = await _friendService.LoadFriendRequest(new FriendRequest(req.Receiver, req.Sender));
            
            // TODO: Wrap in transaction
            await _friendService.DeleteFriendRequest(request);
            if (reciprocalRequest != null)
            {
                await _friendService.DeleteFriendRequest(reciprocalRequest);
            } 

            await _friendService.AddFriendship(req.Receiver, req.Sender);

            var senderFriendlist = await _friendService.LoadFriendlist(req.Sender);
            var currentUserFriendlist = await _friendService.LoadFriendlist(req.Receiver); 
            List<FriendRequest> sentRequests = await _friendService.LoadSentFriendRequests(req.Receiver);
            List<FriendRequest> receivedRequests = await _friendService.LoadReceivedFriendRequests(req.Receiver);
            
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
            var requestsSentByOtherPlayer = await _friendService.LoadSentFriendRequests(req.Sender);
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
            var request = await _friendService.LoadFriendRequest(req) ?? throw new ValidationException("Could not find a friend request to deny.");
            await _friendService.DeleteFriendRequest(request);

            List<FriendRequest> receivedRequests = await _friendService.LoadReceivedFriendRequests(req.Receiver);
            await Clients.Caller.SendAsync(
                FriendResponseType.FriendResponseData.ToString(),
                null,
                null,
                receivedRequests,
                $"Friend request from {req.Sender} denied!"
            );

            var sentRequests = await _friendService.LoadSentFriendRequests(req.Sender);
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
            var currentUserFriendlist = await _friendService.LoadFriendlist(req.Receiver);
            CanBlock(currentUserFriendlist, req.Sender);

            var request = await _friendService.LoadFriendRequest(req) ?? throw new ValidationException("Could not find a friend request to block.");
            await _friendService.DeleteFriendRequest(request);

            await _friendService.AddBlockedPlayer(req.Receiver, req.Sender);
            currentUserFriendlist = await _friendService.LoadFriendlist(req.Receiver);

            List<FriendRequest> receivedRequests = await _friendService.LoadReceivedFriendRequests(req.Receiver);
            await Clients.Caller.SendAsync(
                FriendResponseType.FriendResponseData.ToString(),
                currentUserFriendlist,
                null,
                receivedRequests,
                $"Friend requests from {req.Sender} blocked!"
            );

            var sentRequests = await _friendService.LoadSentFriendRequests(req.Sender);
            await PushFriendResponseDataToPlayer(req.Sender, null, sentRequests);
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync(FriendResponseType.FriendResponseMessage.ToString(), ex.Message);
        }
    }

    public async Task BlockPlayer(string battleTag)
    {
        var currentUser = _connections.GetUser(Context.ConnectionId)?.BattleTag;
        if (currentUser == null)
        {
            return;
        }
        try
        {
            var friendList = await _friendService.LoadFriendlist(currentUser);
            CanBlock(friendList, battleTag);
            await _friendService.AddBlockedPlayer(currentUser, battleTag);
            friendList = await _friendService.LoadFriendlist(currentUser);
            await Clients.Caller.SendAsync(
                FriendResponseType.FriendResponseData.ToString(),
                friendList,
                null,
                null,
                $"Player {battleTag} blocked!"
            );
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
        {
            return;
        }
        try
        {
            var friendList = await _friendService.LoadFriendlist(currentUser);
            
            if (!friendList.IsBlocked(battleTag))
            {
                throw new ValidationException("Could not find a player to unblock.");
            }
            
            await _friendService.RemoveBlockedPlayer(currentUser, battleTag);
            friendList = await _friendService.LoadFriendlist(currentUser);

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
        {
            return;
        }
        try
        {
            await _friendService.RemoveFriendship(currentUser, friend);
            
            var currentUserFriendlist = await _friendService.LoadFriendlist(currentUser);
            var otherUserFriendlist = await _friendService.LoadFriendlist(friend);

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
        FriendlistCache friendList = await _friendService.LoadFriendlist(battleTag);
        if (friendList.Friends.Count == 0)
            return [];

        List<PersonalSetting> personalSettings = await _personalSettingsRepository.LoadMany(friendList.Friends.ToArray());
        Dictionary<string, bool> friendStatus = _connections.GetUsersOnlineStatus(friendList.Friends);

        List<FriendUser> friends = friendStatus
            .Select(x => new FriendUser
            {
                BattleTag = x.Key,
                ProfilePicture = personalSettings.FirstOrDefault(p => p.Id == x.Key)?.ProfilePicture ?? ProfilePicture.Default(),
                IsOnline = x.Value,
            }).ToList();

        return friends ?? [];
    }

    private async Task PushFriendResponseDataToPlayer(
        string battleTag,
        FriendlistCache friendList = null,
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

    private async Task CanMakeFriendRequest(FriendlistCache friendList, FriendRequest req)
    {
        if (friendList.BlockAllRequests || friendList.IsBlocked(req.Sender))
        {
            throw new ValidationException("This player is not accepting friend requests.");
        }
        if (friendList.IsFriend(req.Sender))
        {
            throw new ValidationException("You are already friends with this player.");
        }
        var requestAlreadyExists = await _friendService.FriendRequestExists(req);
        if (requestAlreadyExists)
        {
            throw new ValidationException("You have already requested to be friends with this player.");
        }
    }

    private static void CanBlock(FriendlistCache friendList, string battleTag)
    {
        if (friendList.IsBlocked(battleTag))
        {
            throw new ValidationException("You have already blocked this player.");
        }
        if (friendList.IsFriend(battleTag))
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
        var friendList = await _friendService.LoadFriendlist(battleTag);
        var onlineFriends = friendList
            .Friends.Where(tag => _connections.IsUserOnline(tag))
            .Select(tag => _connections.GetConnectionId(tag))
            .SelectMany(connection => connection);

        await Clients.Clients(onlineFriends).SendAsync(FriendResponseType.FriendOnlineStatus.ToString(), battleTag, isOnline);
    }
}
