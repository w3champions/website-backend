using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using W3ChampionsStatisticService.Friends;
using W3ChampionsStatisticService.NetworkDiagnostics;
using W3ChampionsStatisticService.NetworkDiagnostics.Delivery;
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
    INetworkDiagnosticsDelivery netDiagDelivery,
    IAdminRoster adminRoster,
    TimeProvider time
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

    private readonly ConnectionMapping _connections = connections;
    private readonly IHttpContextAccessor _contextAccessor = contextAccessor;
    private readonly IFriendRequestCache _friendRequestCache = friendRequestCache;
    private readonly IPersonalSettingsRepository _personalSettingsRepository = personalSettingsRepository;
    private readonly IFriendCommandHandler _friendCommandHandler = friendCommandHandler;
    private readonly TracingService _tracingService = tracingService;
    private readonly IBattleTagResolver _battleTagResolver = battleTagResolver;
    private readonly IRelationshipChangeNotifier _relationshipChangeNotifier = relationshipChangeNotifier;
    private readonly ITicketStore _ticketStore = ticketStore;
    private readonly INetworkDiagnosticsDelivery _netDiagDelivery = netDiagDelivery;
    private readonly IAdminRoster _adminRoster = adminRoster;
    private readonly TimeProvider _time = time;

    /// <summary>Refusal copy sent when a user tries to block an admin (exact wire text).</summary>
    public const string AdminsCantBeBlockedMessage = "Admins can't be blocked.";

    /// <summary>Upper bound for the on-connect diagnostics delivery, measured on the injected <see cref="TimeProvider"/>.</summary>
    internal static TimeSpan NetDiagOnConnectBound => NetDiagConstants.OnConnectDeliveryBound;

    [NoTrace]
    public override async Task OnConnectedAsync()
    {
        await _tracingService.ExecuteWithSpanAsync(this, async () =>
        {
            // Ticket-only auth (WB-1): access_token must be a ticket minted via POST /auth/session.
            // A raw JWT (or anything else) is rejected, like chat-service's ChatHub. This connection
            // holds the ticket until OnDisconnectedAsync releases it (redemption rules on TicketStore).
            var accessToken = _contextAccessor?.HttpContext?.Request.Query["access_token"].ToString();
            W3CUserAuthenticationDto w3cUserAuthentication = null;
            if (!string.IsNullOrEmpty(accessToken))
            {
                if (_ticketStore.TryRedeem(accessToken, Context.ConnectionId, _time.GetUtcNow().UtcDateTime, out var ticketIdentity))
                {
                    w3cUserAuthentication = ticketIdentity;
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
            await DeliverNetworkDiagnosticsAsync(user.BattleTag);
        }, forceNewRoot: true);
        await base.OnConnectedAsync();
    }

    internal async Task LoginAsAuthenticated(WebSocketUser user)
    {
        _connections.Add(Context.ConnectionId, user);
        await Clients.Caller.SendAsync(WebsiteBackendSocketResponseType.Connected.ToString());
    }

    // Diagnostics must never cost the user the hub connection (Review Focus 5): log and carry on. The catch
    // contains exceptions and the bound contains hangs: SignalR dispatches this connection's invocations only after
    // OnConnectedAsync returns, so a stalled Mongo call must not hold it.
    private async Task DeliverNetworkDiagnosticsAsync(string battleTag)
    {
        try
        {
            var delivery = _netDiagDelivery.OnUserConnectedAsync(battleTag);
            try
            {
                await delivery.WaitAsync(NetDiagOnConnectBound, _time);
            }
            catch (TimeoutException) when (!delivery.IsCompleted)
            {
                // The delivery pushes through IHubContext, so it can finish after this method has returned.
                Log.Warning("Network diagnostics on-connect delivery for {BattleTag} is taking too long; it continues in the background", battleTag);
                _ = delivery.ContinueWith(
                    failed => Log.Warning(failed.Exception!.GetBaseException(), "Network diagnostics on-connect delivery failed for {BattleTag}", battleTag),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (Exception e)
        {
            Log.Warning(e, "Network diagnostics on-connect delivery failed for {BattleTag}", battleTag);
        }
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
        await SendFriendResponseData(Clients.Caller, await _adminRoster.GetSnapshotAsync(), friendList, sentRequests, receivedRequests);
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
            await CanMakeFriendRequest(receiverFriendlist, await _adminRoster.GetSnapshotAsync(), req);
            await _friendCommandHandler.CreateFriendRequest(req);
            sentRequests.Add(req);

            await Clients.Caller.SendAsync(
                FriendResponseType.FriendResponseData.ToString(),
                null,
                sentRequests,
                null,
                $"Friend request sent to {req.Receiver}!"
            );

            await AfterCommitBestEffort("push received requests to the receiver", async () =>
            {
                var requestsReceivedByOtherPlayer = await _friendRequestCache.LoadReceivedFriendRequests(req.Receiver);
                await PushFriendRequestsToPlayer(req.Receiver, null, requestsReceivedByOtherPlayer);
            });
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

            await AfterCommitBestEffort("push received requests to the receiver", async () =>
            {
                var requestsReceivedByOtherPlayer = await _friendRequestCache.LoadReceivedFriendRequests(req.Receiver);
                await PushFriendRequestsToPlayer(req.Receiver, null, requestsReceivedByOtherPlayer);
            });
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
            var admins = await _adminRoster.GetSnapshotAsync();
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
            await SendFriendResponseData(
                Clients.Caller,
                admins,
                currentUserFriendlist,
                sentRequests,
                receivedRequests,
                $"Friend request from {req.Sender} accepted!"
            );

            await AfterCommitBestEffort("refresh the caller's friends with pictures", async () =>
            {
                List<FriendUser> receiverFriends = await GetFriends(req.Receiver);
                await Clients.Caller.SendAsync(FriendResponseType.FriendsWithPictures.ToString(), receiverFriends);
            });
            await AfterCommitBestEffort("push friends with pictures to the sender", () => PushFriendsWithPicturesToPlayer(req.Sender));
            await AfterCommitBestEffort("push friend data to the sender", async () =>
            {
                var requestsSentByOtherPlayer = await _friendRequestCache.LoadSentFriendRequests(req.Sender);
                await PushFriendResponseDataToPlayer(req.Sender, admins, senderFriendlist, requestsSentByOtherPlayer);
            });
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

            await AfterCommitBestEffort("push sent requests to the sender", async () =>
            {
                var sentRequests = await _friendRequestCache.LoadSentFriendRequests(req.Sender);
                await PushFriendRequestsToPlayer(req.Sender, sentRequests, null);
            });
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
            // One roster view for the refusal, CanBlock and the response: a refresh mid-operation must not
            // let the refusal and the effective list disagree about the sender.
            var admins = await _adminRoster.GetSnapshotAsync();
            var currentUserFriendlist = await _friendCommandHandler.LoadFriendList(req.Receiver);
            // Friend requests are not influenced by admin status: a request from an admin is still
            // declined here, only the block step is refused.
            var senderIsAdmin = admins.IsAdmin(req.Sender);
            if (!senderIsAdmin)
            {
                CanBlock(WithoutBlockedAdmins(currentUserFriendlist, admins), req.Sender);
            }

            var request = await _friendRequestCache.LoadFriendRequest(req) ?? throw new ValidationException("Could not find a friend request to block.");
            await _friendCommandHandler.DeleteFriendRequest(request);

            if (!senderIsAdmin)
            {
                currentUserFriendlist.BlockedBattleTags.Add(req.Sender);
                await _friendCommandHandler.UpsertFriendList(currentUserFriendlist);
                TryNotifyRelationshipChange(RelationshipChangeType.Block, req.Receiver, req.Sender);
            }

            List<FriendRequest> receivedRequests = await _friendRequestCache.LoadReceivedFriendRequests(req.Receiver);
            await SendFriendResponseData(
                Clients.Caller,
                admins,
                currentUserFriendlist,
                null,
                receivedRequests,
                senderIsAdmin ? AdminsCantBeBlockedMessage : $"Friend requests from {req.Sender} blocked!"
            );

            await AfterCommitBestEffort("push sent requests to the sender", async () =>
            {
                var sentRequests = await _friendRequestCache.LoadSentFriendRequests(req.Sender);
                await PushFriendRequestsToPlayer(req.Sender, sentRequests, null);
            });
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync(FriendResponseType.FriendResponseMessage.ToString(), ex.Message);
        }
    }

    /// <summary>Returns <c>true</c> iff the block was persisted (even if the follow-up response send fails).
    /// Every refusal sends its message to the caller BEFORE returning: the launcher reads
    /// friendResponseMessage as soon as this invocation resolves.</summary>
    public async Task<bool> BlockPlayer(string battleTag)
    {
        var currentUser = _connections.GetUser(Context.ConnectionId)?.BattleTag;
        if (currentUser == null)
        {
            return false;
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
            return false;
        }
        battleTag = canonicalBattleTag;

        var blocked = false;
        try
        {
            // One roster view for the refusal, CanBlock and the response (see BlockIncomingFriendRequest).
            var admins = await _adminRoster.GetSnapshotAsync();
            if (admins.IsAdmin(battleTag))
                throw new ValidationException(AdminsCantBeBlockedMessage);

            var friendList = await _friendCommandHandler.LoadFriendList(currentUser);
            CanBlock(WithoutBlockedAdmins(friendList, admins), battleTag);
            friendList.BlockedBattleTags.Add(battleTag);
            await _friendCommandHandler.UpsertFriendList(friendList);
            blocked = true;
            TryNotifyRelationshipChange(RelationshipChangeType.Block, currentUser, battleTag);
            await SendFriendResponseData(
                Clients.Caller,
                admins,
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
        return blocked;
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

            // Case-insensitive, and every legacy case variant goes: a leftover variant would keep blocking.
            var removed = friendList.BlockedBattleTags.RemoveAll(bTag => string.Equals(bTag, battleTag, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                throw new ValidationException("Could not find a player to unblock.");
            }

            await _friendCommandHandler.UpsertFriendList(friendList);
            TryNotifyRelationshipChange(RelationshipChangeType.Unblock, currentUser, battleTag);

            await SendFriendResponseData(
                Clients.Caller,
                await _adminRoster.GetSnapshotAsync(),
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
            var admins = await _adminRoster.GetSnapshotAsync();
            var currentUserFriendlist = await _friendCommandHandler.LoadFriendList(currentUser);
            currentUserFriendlist = await _friendCommandHandler.RemoveFriend(currentUserFriendlist, friend);

            var otherUserFriendlist = await _friendCommandHandler.LoadFriendList(friend);
            otherUserFriendlist = await _friendCommandHandler.RemoveFriend(otherUserFriendlist, currentUser);
            TryNotifyRelationshipChange(RelationshipChangeType.FriendRemove, currentUser, friend);

            await SendFriendResponseData(
                Clients.Caller,
                admins,
                currentUserFriendlist,
                null,
                null,
                $"Removed {friend} from friends."
            );

            await AfterCommitBestEffort("refresh the caller's friends with pictures", async () =>
            {
                List<FriendUser> currentUserFriends = await GetFriends(currentUser);
                await Clients.Caller.SendAsync(FriendResponseType.FriendsWithPictures.ToString(), currentUserFriends);
            });
            await AfterCommitBestEffort("push friends with pictures to the removed friend", () => PushFriendsWithPicturesToPlayer(friend));
            await AfterCommitBestEffort("push friend data to the removed friend", () => PushFriendResponseDataToPlayer(friend, admins, otherUserFriendlist));
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
        AdminSnapshot admins,
        Friendlist friendList,
        List<FriendRequest> sentRequests = null
    )
    {
        var player = _connections.GetUsers().FirstOrDefault(x => x.BattleTag == battleTag);
        if (player?.ConnectionId == null)
            return;
        await SendFriendResponseData(Clients.Client(player.ConnectionId), admins, friendList, sentRequests, null);
    }

    /// <summary>A FriendResponseData without a Friendlist (only request lists), so no roster read.</summary>
    private async Task PushFriendRequestsToPlayer(
        string battleTag,
        List<FriendRequest> sentRequests,
        List<FriendRequest> receivedRequests
    )
    {
        var player = _connections.GetUsers().FirstOrDefault(x => x.BattleTag == battleTag);
        if (player?.ConnectionId == null)
            return;
        await Clients.Client(player.ConnectionId).SendAsync(FriendResponseType.FriendResponseData.ToString(), null, sentRequests, receivedRequests, null);
    }

    /// <summary>Every FriendResponseData carrying a Friendlist goes through here: admins are hidden from
    /// the outbound BlockedBattleTags via a projected copy (the loaded Friendlist is cached and must never
    /// be filtered in place). <paramref name="admins"/> is the operation's single roster snapshot.</summary>
    private async Task SendFriendResponseData(
        IClientProxy target,
        AdminSnapshot admins,
        Friendlist friendList,
        List<FriendRequest> sentRequests,
        List<FriendRequest> receivedRequests,
        string message = null
    )
    {
        var outbound = WithoutBlockedAdmins(friendList, admins);
        await target.SendAsync(FriendResponseType.FriendResponseData.ToString(), outbound, sentRequests, receivedRequests, message);
    }

    /// <summary>The effective view of a stored Friendlist: admins can't be blocked, so a stored block of an
    /// admin (made before that rule, or before the player became an admin) is dormant. Every outbound list
    /// AND every server-side block decision reads this view; the stored list is never filtered, so such a
    /// block takes effect again if the player stops being an admin. <paramref name="admins"/> is the
    /// operation's single roster snapshot (never re-read mid-operation); the roster fails open to
    /// <see cref="AdminSnapshot.Empty"/>, i.e. the stored list as-is.</summary>
    private static Friendlist WithoutBlockedAdmins(Friendlist friendList, AdminSnapshot admins) =>
        friendList == null ? null : FriendlistProjection.WithoutBlockedAdmins(friendList, admins.Tags);

    private async Task PushFriendsWithPicturesToPlayer(string battleTag)
    {
        var player = _connections.GetUsers().FirstOrDefault(x => x.BattleTag == battleTag);
        if (player?.ConnectionId == null)
            return;
        List<FriendUser> friends = await GetFriends(battleTag);
        await Clients.Client(player.ConnectionId).SendAsync(FriendResponseType.FriendsWithPictures.ToString(), friends);
    }

    /// <summary>Reads the receiver's effective blocks: a dormant block of an admin sender doesn't count.
    /// Admin status is otherwise no privilege (BlockAllRequests still applies).</summary>
    private async Task CanMakeFriendRequest(Friendlist storedFriendList, AdminSnapshot admins, FriendRequest req)
    {
        var friendList = WithoutBlockedAdmins(storedFriendList, admins);
        if (friendList.BlockAllRequests || friendList.BlockedBattleTags.Contains(req.Sender, StringComparer.OrdinalIgnoreCase))
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

    /// <summary>Takes the effective view (<see cref="WithoutBlockedAdmins(Friendlist, AdminSnapshot)"/>), never the stored list.
    /// BattleTags match case-insensitively, as in the admin projection.</summary>
    private static void CanBlock(Friendlist friendList, string battleTag)
    {
        if (friendList.BlockedBattleTags.Contains(battleTag, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationException("You have already blocked this player.");
        }
        if (friendList.Friends.Contains(battleTag, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationException("You cannot block a player you are friends with.");
        }
    }

    /// <summary>A follow-up after the change was persisted and the caller was sent its success (counterpart
    /// pushes, the caller's friends-with-pictures refresh). Never throws: the hub methods' catch-all would
    /// otherwise send the caller an error FriendResponseMessage that overrides the success, so the client
    /// would report a failure for a change that was saved. Each step is isolated so one failure doesn't
    /// skip the rest.</summary>
    private static async Task AfterCommitBestEffort(string step, Func<Task> followUp)
    {
        try { await followUp(); }
        catch (Exception e) { Log.Warning(e, "Friend hub post-commit step failed (change already saved): {Step}", step); }
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
        // Free the ticket so a reconnect inside its TTL can redeem it again. If OnConnectedAsync threw
        // after redeeming, SignalR skips this method and the ticket stays held: its next presentation
        // revokes it, and the launcher mints a new one.
        _ticketStore.Release(Context.ConnectionId);

        var user = _connections.GetUser(Context.ConnectionId);
        if (user != null)
        {
            _connections.Remove(Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
