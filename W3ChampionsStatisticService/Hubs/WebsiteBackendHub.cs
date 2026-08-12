using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using W3ChampionsStatisticService.Friends;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Sessions;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3C.Domain.ChatService;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Services;
using static W3ChampionsStatisticService.Filters.SignalRTraceContextFilter;

namespace W3ChampionsStatisticService.Hubs;

[Trace]
public class WebsiteBackendHub(
    ConnectionMapping connections,
    IHttpContextAccessor contextAccessor,
    IFriendRequestCache friendRequestCache,
    IPersonalSettingsRepository personalSettingsRepository,
    IFriendCommandHandler friendCommandHandler,
    TracingService tracingService,
    IBattleTagResolver battleTagResolver,
    IRelationshipChangeNotifier relationshipChangeNotifier,
    ITicketStore ticketStore,
    IW3CAuthenticationService authenticationService
) : Hub
{
    static WebsiteBackendHub()
    {
        // Check if any of the public handlers have no arguments, as we need them to have at least one argument due to tracing requirements.
        var methods = typeof(WebsiteBackendHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var allowedZeroArgMethods = new HashSet<string> { "LoadFriendListAndRequests", "LoadFriendsWithPictures", "MarkIncomingFriendRequestsSeen" };
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

    private readonly ConnectionMapping _connections = connections;
    private readonly IHttpContextAccessor _contextAccessor = contextAccessor;
    private readonly IFriendRequestCache _friendRequestCache = friendRequestCache;
    private readonly IPersonalSettingsRepository _personalSettingsRepository = personalSettingsRepository;
    private readonly IFriendCommandHandler _friendCommandHandler = friendCommandHandler;
    private readonly TracingService _tracingService = tracingService;
    private readonly IBattleTagResolver _battleTagResolver = battleTagResolver;
    private readonly IRelationshipChangeNotifier _relationshipChangeNotifier = relationshipChangeNotifier;
    private readonly ITicketStore _ticketStore = ticketStore;
    private readonly IW3CAuthenticationService _authenticationService = authenticationService;


    [NoTrace]
    public override async Task OnConnectedAsync()
    {
        await _tracingService.ExecuteWithSpanAsync(this, async () =>
        {
            // Ticket-first auth (WB-1) with a TEMPORARY raw-JWT fallback.
            //
            // The original plan was a hard cutover to single-use tickets, lock-step with a forced
            // launcher update. That update never shipped: the last launcher release (v1.6.5,
            // 2026-06-16) predates the ticket flow entirely, so from the moment this hub's cutover
            // deployed, every player on the released launcher presented a raw JWT, was rejected
            // here, and lost friends/presence — a fleet-wide outage, not a coordinated window.
            //
            // Bridge: try the ticket path first (updated launchers), then fall back to validating
            // access_token as a raw JWT (released launchers), exactly as this hub did before the
            // cutover. validateLifetime: false mirrors the pre-cutover behavior.
            //
            // REMOVE the fallback once a launcher release containing the ticket mint
            // (launcher-e #833) has shipped AND its forced-update rollout has completed.
            var accessToken = _contextAccessor?.HttpContext?.Request.Query["access_token"].ToString();
            W3CUserAuthenticationDto w3cUserAuthentication = null;
            if (!string.IsNullOrEmpty(accessToken))
            {
                if (_ticketStore.TryConsume(accessToken, DateTime.UtcNow, out var ticketIdentity))
                {
                    w3cUserAuthentication = ticketIdentity;
                }
                else
                {
                    // GetUserByToken THROWS on a malformed/invalid token (FromJWT validates bare);
                    // every REST filter that calls it wraps in try/catch and 401s. Mirror that here:
                    // an updated launcher whose single-use ticket was already consumed lands on this
                    // path with a hex ticket, which must reject cleanly (AuthorizationFailed below),
                    // not surface as a hub exception.
                    try
                    {
                        w3cUserAuthentication = _authenticationService.GetUserByToken(accessToken, false);
                    }
                    catch (Exception)
                    {
                        w3cUserAuthentication = null;
                    }
                }
            }
            if (w3cUserAuthentication == null)
            {
                await Clients.Caller.SendAsync("AuthorizationFailed");
                Context.Abort();
                return;
            }
            WebSocketUser user = new() { BattleTag = w3cUserAuthentication.BattleTag, ConnectionId = Context.ConnectionId };
            await LoginAsAuthenticated(user);
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
        Friendlist friendList = await _friendCommandHandler.LoadFriendList(currentUser);
        List<FriendRequest> sentRequests = await _friendRequestCache.LoadSentFriendRequests(currentUser);
        List<FriendRequest> receivedRequests = await _friendRequestCache.LoadReceivedFriendRequests(currentUser);
        await Clients.Caller.SendAsync(FriendResponseType.FriendResponseData.ToString(), friendList, sentRequests, receivedRequests);
    }

    public async Task MarkIncomingFriendRequestsSeen()
    {
        await this.MarkIncomingFriendRequestsSeenTraced(new PreventZeroArgumentHandler());
    }

    // The receiver acknowledges having seen their incoming friend requests
    // (e.g. opened the list). Stamps SeenAt on every not-yet-seen request, then
    // returns the refreshed received list so the caller's state stays in sync.
    public async Task MarkIncomingFriendRequestsSeenTraced(PreventZeroArgumentHandler _)
    {
        var currentUser = _connections.GetUser(Context.ConnectionId)?.BattleTag;
        if (currentUser == null)
        {
            return;
        }
        await _friendCommandHandler.MarkIncomingFriendRequestsSeen(currentUser);
        List<FriendRequest> receivedRequests = await _friendRequestCache.LoadReceivedFriendRequests(currentUser);
        await Clients.Caller.SendAsync(FriendResponseType.FriendResponseData.ToString(), null, null, receivedRequests);
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
        var jwtBattleTag = _connections.GetUser(Context.ConnectionId)?.BattleTag;
        if (jwtBattleTag == null)
        {
            await Clients.Caller.SendAsync("BattleTagResolutionError", new { reason = "not_authenticated" });
            return;
        }
        req.Sender = jwtBattleTag;

        var canonicalReceiver = await _battleTagResolver.ResolveCanonical(req.Receiver);
        if (canonicalReceiver == null || canonicalReceiver != req.Receiver)
        {
            await Clients.Caller.SendAsync("BattleTagResolutionError", new
            {
                reason = canonicalReceiver == null ? "user_not_found" : "non_canonical_battletag",
                input = req.Receiver,
                canonical = canonicalReceiver
            });
            return;
        }
        req.Receiver = canonicalReceiver;

        try
        {
            PersonalSetting personalSetting =
                await _personalSettingsRepository.Find(req.Receiver) ?? throw new ValidationException($"Player {req.Receiver} not found.");

            if (req.Sender == req.Receiver)
            {
                throw new ValidationException("Cannot request yourself as a friend.");
            }
            var sentRequests = await _friendRequestCache.LoadSentFriendRequests(req.Sender);
            if (sentRequests.Count > 10)
            {
                throw new ValidationException("You have too many pending friend requests.");
            }

            var receiverFriendlist = await _friendCommandHandler.LoadFriendList(req.Receiver);
            await CanMakeFriendRequest(receiverFriendlist, req);
            await _friendCommandHandler.CreateFriendRequest(req);
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
        var jwtBattleTag = _connections.GetUser(Context.ConnectionId)?.BattleTag;
        if (jwtBattleTag == null)
        {
            await Clients.Caller.SendAsync("BattleTagResolutionError", new { reason = "not_authenticated" });
            return;
        }
        req.Sender = jwtBattleTag;

        var canonicalReceiver = await _battleTagResolver.ResolveCanonical(req.Receiver);
        if (canonicalReceiver == null || canonicalReceiver != req.Receiver)
        {
            await Clients.Caller.SendAsync("BattleTagResolutionError", new
            {
                reason = canonicalReceiver == null ? "user_not_found" : "non_canonical_battletag",
                input = req.Receiver,
                canonical = canonicalReceiver
            });
            return;
        }
        req.Receiver = canonicalReceiver;

        try
        {
            var request = await _friendRequestCache.LoadFriendRequest(req) ?? throw new ValidationException("Could not find a friend request to delete.");
            await _friendCommandHandler.DeleteFriendRequest(request);

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
        var jwtBattleTag = _connections.GetUser(Context.ConnectionId)?.BattleTag;
        if (jwtBattleTag == null)
        {
            await Clients.Caller.SendAsync("BattleTagResolutionError", new { reason = "not_authenticated" });
            return;
        }
        req.Receiver = jwtBattleTag;

        var canonicalSender = await _battleTagResolver.ResolveCanonical(req.Sender);
        if (canonicalSender == null || canonicalSender != req.Sender)
        {
            await Clients.Caller.SendAsync("BattleTagResolutionError", new
            {
                reason = canonicalSender == null ? "user_not_found" : "non_canonical_battletag",
                input = req.Sender,
                canonical = canonicalSender
            });
            return;
        }
        req.Sender = canonicalSender;

        try
        {
            var currentUserFriendlist = await _friendCommandHandler.LoadFriendList(req.Receiver);
            var senderFriendlist = await _friendCommandHandler.LoadFriendList(req.Sender);

            var request = await _friendRequestCache.LoadFriendRequest(req) ?? throw new ValidationException("Could not find a friend request to accept.");
            await _friendCommandHandler.DeleteFriendRequest(request);

            var reciprocalRequest = await _friendRequestCache.LoadFriendRequest(new FriendRequest(req.Receiver, req.Sender));
            await _friendCommandHandler.DeleteFriendRequest(reciprocalRequest);

            currentUserFriendlist = await _friendCommandHandler.AddFriend(currentUserFriendlist, req.Sender);
            senderFriendlist = await _friendCommandHandler.AddFriend(senderFriendlist, req.Receiver);
            TryNotifyRelationshipChange(RelationshipChangeType.FriendAdd, req.Receiver, req.Sender);

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
        var jwtBattleTag = _connections.GetUser(Context.ConnectionId)?.BattleTag;
        if (jwtBattleTag == null)
        {
            await Clients.Caller.SendAsync("BattleTagResolutionError", new { reason = "not_authenticated" });
            return;
        }
        req.Receiver = jwtBattleTag;

        var canonicalSender = await _battleTagResolver.ResolveCanonical(req.Sender);
        if (canonicalSender == null || canonicalSender != req.Sender)
        {
            await Clients.Caller.SendAsync("BattleTagResolutionError", new
            {
                reason = canonicalSender == null ? "user_not_found" : "non_canonical_battletag",
                input = req.Sender,
                canonical = canonicalSender
            });
            return;
        }
        req.Sender = canonicalSender;

        try
        {
            var request = await _friendRequestCache.LoadFriendRequest(req) ?? throw new ValidationException("Could not find a friend request to deny.");
            await _friendCommandHandler.DeleteFriendRequest(request);

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
        var jwtBattleTag = _connections.GetUser(Context.ConnectionId)?.BattleTag;
        if (jwtBattleTag == null)
        {
            await Clients.Caller.SendAsync("BattleTagResolutionError", new { reason = "not_authenticated" });
            return;
        }
        req.Receiver = jwtBattleTag;

        var canonicalSender = await _battleTagResolver.ResolveCanonical(req.Sender);
        if (canonicalSender == null || canonicalSender != req.Sender)
        {
            await Clients.Caller.SendAsync("BattleTagResolutionError", new
            {
                reason = canonicalSender == null ? "user_not_found" : "non_canonical_battletag",
                input = req.Sender,
                canonical = canonicalSender
            });
            return;
        }
        req.Sender = canonicalSender;

        try
        {
            var currentUserFriendlist = await _friendCommandHandler.LoadFriendList(req.Receiver);
            CanBlock(currentUserFriendlist, req.Sender);

            var request = await _friendRequestCache.LoadFriendRequest(req) ?? throw new ValidationException("Could not find a friend request to block.");
            await _friendCommandHandler.DeleteFriendRequest(request);

            currentUserFriendlist.BlockedBattleTags.Add(req.Sender);
            await _friendCommandHandler.UpsertFriendList(currentUserFriendlist);
            TryNotifyRelationshipChange(RelationshipChangeType.Block, req.Receiver, req.Sender);

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

    public async Task BlockPlayer(string battleTag)
    {
        var currentUser = _connections.GetUser(Context.ConnectionId)?.BattleTag;
        if (currentUser == null)
        {
            return;
        }

        var canonicalBattleTag = await _battleTagResolver.ResolveCanonical(battleTag);
        if (canonicalBattleTag == null || canonicalBattleTag != battleTag)
        {
            await Clients.Caller.SendAsync("BattleTagResolutionError", new
            {
                reason = canonicalBattleTag == null ? "user_not_found" : "non_canonical_battletag",
                input = battleTag,
                canonical = canonicalBattleTag
            });
            return;
        }
        battleTag = canonicalBattleTag;

        try
        {
            var friendList = await _friendCommandHandler.LoadFriendList(currentUser);
            CanBlock(friendList, battleTag);
            friendList.BlockedBattleTags.Add(battleTag);
            await _friendCommandHandler.UpsertFriendList(friendList);
            TryNotifyRelationshipChange(RelationshipChangeType.Block, currentUser, battleTag);
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

        var canonicalBattleTag = await _battleTagResolver.ResolveCanonical(battleTag);
        if (canonicalBattleTag == null || canonicalBattleTag != battleTag)
        {
            await Clients.Caller.SendAsync("BattleTagResolutionError", new
            {
                reason = canonicalBattleTag == null ? "user_not_found" : "non_canonical_battletag",
                input = battleTag,
                canonical = canonicalBattleTag
            });
            return;
        }
        battleTag = canonicalBattleTag;

        try
        {
            var friendList = await _friendCommandHandler.LoadFriendList(currentUser);

            var itemToRemove =
                friendList.BlockedBattleTags.SingleOrDefault(bTag => bTag == battleTag) ?? throw new ValidationException("Could not find a player to unblock.");
            friendList.BlockedBattleTags.Remove(itemToRemove);

            await _friendCommandHandler.UpsertFriendList(friendList);
            TryNotifyRelationshipChange(RelationshipChangeType.Unblock, currentUser, battleTag);

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

        var canonicalFriend = await _battleTagResolver.ResolveCanonical(friend);
        if (canonicalFriend == null || canonicalFriend != friend)
        {
            await Clients.Caller.SendAsync("BattleTagResolutionError", new
            {
                reason = canonicalFriend == null ? "user_not_found" : "non_canonical_battletag",
                input = friend,
                canonical = canonicalFriend
            });
            return;
        }
        friend = canonicalFriend;

        try
        {
            var currentUserFriendlist = await _friendCommandHandler.LoadFriendList(currentUser);
            currentUserFriendlist = await _friendCommandHandler.RemoveFriend(currentUserFriendlist, friend);

            var otherUserFriendlist = await _friendCommandHandler.LoadFriendList(friend);
            otherUserFriendlist = await _friendCommandHandler.RemoveFriend(otherUserFriendlist, currentUser);
            TryNotifyRelationshipChange(RelationshipChangeType.FriendRemove, currentUser, friend);

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
        Friendlist friendList = await _friendCommandHandler.LoadFriendList(battleTag);
        if (friendList.Friends.Count == 0)
            return [];

        List<PersonalSetting> personalSettings = await _personalSettingsRepository.LoadMany([.. friendList.Friends]);
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

    // Best-effort chat-service cache-invalidation ping (spec §6/§14). NEVER throws and NEVER
    // blocks: the notifier enqueues in the background; this guard is defense-in-depth so no
    // notifier fault can convert a succeeded friend/block action into a user-visible error.
    private void TryNotifyRelationshipChange(RelationshipChangeType type, string actor, string target)
    {
        try { _relationshipChangeNotifier.NotifyChange(type, actor, target); }
        catch (Exception e) { Log.Warning(e, "Relationship change-ping dispatch failed: {Type} {Actor}/{Target}", type, actor, target); }
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        var user = _connections.GetUser(Context.ConnectionId);
        if (user != null)
        {
            _connections.Remove(Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
