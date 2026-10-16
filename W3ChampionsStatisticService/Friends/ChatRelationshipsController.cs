using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Friends;

[ApiController]
[Route("api/players")]
public class ChatRelationshipsController(IFriendRepository friendRepository, IAdminRoster adminRoster) : ControllerBase
{
    private readonly IFriendRepository _friendRepository = friendRepository;
    private readonly IAdminRoster _adminRoster = adminRoster;

    /// <summary>Chat-service relationship snapshot (spec §6). Wire shape pinned by C5's
    /// WebsiteBackendRelationshipSource: lowercase {"friends":[],"blocked":[]}, both keys
    /// always present/non-null — a malformed 200 makes chat fail closed platform-wide.
    /// BlockAllRequests is deliberately NOT surfaced (friend-request gate, not a chat block).
    /// Admins are omitted from "blocked" (admins can't be blocked); the stored list is untouched.</summary>
    [HttpGet("{battleTag}/chat-relationships")]
    [ChatServiceSecretAuth]
    public async Task<IActionResult> GetChatRelationships(string battleTag)
    {
        Friendlist friendlist = await _friendRepository.LoadFriendlistOrNull(battleTag);
        var blocked = FriendlistProjection.WithoutBlockedAdmins(friendlist, (await _adminRoster.GetSnapshotAsync()).Tags)?.BlockedBattleTags;
        return Ok(new ChatRelationshipsDto(
            friendlist?.Friends ?? [],
            blocked ?? []));
    }
}

public class ChatRelationshipsDto(List<string> friends, List<string> blocked)
{
    public List<string> Friends { get; } = friends ?? [];   // wire: "friends"
    public List<string> Blocked { get; } = blocked ?? [];   // wire: "blocked"
}
