using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using W3ChampionsStatisticService.Ports;
using System.ComponentModel.DataAnnotations;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.Friends;

[ApiController]
[Route("api/friends")]
public class FriendsController : ControllerBase
{
    private readonly IFriendRepository _friendRepository;
    private readonly IPlayerRepository _playerRepository;
    private readonly IPersonalSettingsRepository _personalSettingsRepository;

    public FriendsController(
        IFriendRepository friendRepository,
        IPlayerRepository playerRepository,
        IPersonalSettingsRepository personalSettingsRepository
    )
    {
        _friendRepository = friendRepository;
        _playerRepository = playerRepository;
        _personalSettingsRepository = personalSettingsRepository;
    }

    [HttpGet("{battleTag}")]
    [CheckIfBattleTagBelongsToAuthCode]
    public async Task<IActionResult> LoadFriendlist(string battleTag)
    {
        var friendlist = await _friendRepository.LoadFriendlist(battleTag);
        return Ok(friendlist);
    }

    [HttpPost("{battleTag}/make-request")]
    [CheckIfBattleTagBelongsToAuthCode]
    public async Task<IActionResult> MakeFriendRequest(string battleTag, [FromBody] string otherBattleTag)
    {
        try {
            var player = await _playerRepository.LoadPlayerProfile(otherBattleTag);
            if (player == null) {
                return BadRequest($"Player {otherBattleTag} not found.");
            }
            if (battleTag.ToLower() == otherBattleTag.ToLower()) {
                return BadRequest($"Cannot request yourself as a friend.");
            }
            var allRequestsMadeByPlayer = await _friendRepository.LoadAllFriendRequestsSentByPlayer(battleTag);
            if (allRequestsMadeByPlayer.Count() > 10) {
                return BadRequest($"You have too many pending friend requests.");
            }
            var request = new FriendRequest(battleTag, otherBattleTag);
            var otherUserFriendlist = await _friendRepository.LoadFriendlist(otherBattleTag);
            await CanMakeFriendRequest(otherUserFriendlist, request);
            await _friendRepository.CreateFriendRequest(request);

            return Ok($"Friend request sent to {otherBattleTag}!");
        } catch (ValidationException ex) {
            return BadRequest(ex.Message);
        } catch (Exception ex) {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("{battleTag}/accept-request")]
    [CheckIfBattleTagBelongsToAuthCode]
    public async Task<IActionResult> AcceptFriendRequest(string battleTag, [FromBody] string otherBattleTag)
    {
        try {
            var currentUserFriendlist = await _friendRepository.LoadFriendlist(battleTag);
            var otherUserFriendlist = await _friendRepository.LoadFriendlist(otherBattleTag);
            var request = await _friendRepository.LoadFriendRequest(otherBattleTag, battleTag);

            if (request == null) {
                return BadRequest("Could not find a friend request to accept.");
            }

            await _friendRepository.DeleteFriendRequest(request);

            if (!currentUserFriendlist.Friends.Contains(otherBattleTag)) {
                currentUserFriendlist.Friends.Add(otherBattleTag);
            }
            await _friendRepository.UpsertFriendlist(currentUserFriendlist);

            if (!otherUserFriendlist.Friends.Contains(battleTag)) {
                otherUserFriendlist.Friends.Add(battleTag);
            }

            var reciprocalRequest = await _friendRepository.LoadFriendRequest(battleTag, otherBattleTag);
            if (reciprocalRequest != null) {
                await _friendRepository.DeleteFriendRequest(reciprocalRequest);
            }

            await _friendRepository.UpsertFriendlist(otherUserFriendlist);
            return Ok($"Friend request from {otherBattleTag} accepted!");
        } catch (Exception ex) {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("{battleTag}/deny-request")]
    [CheckIfBattleTagBelongsToAuthCode]
    public async Task<IActionResult> DenyFriendRequest(string battleTag, [FromBody] string otherBattleTag)
    {
        try {
            var request = await _friendRepository.LoadFriendRequest(otherBattleTag, battleTag);

            if (request == null) {
                return BadRequest("Could not find a friend request to deny.");
            }

            await _friendRepository.DeleteFriendRequest(request);

            return Ok($"Friend request from {otherBattleTag} denied!");
        } catch (Exception ex) {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("{battleTag}/block-request")]
    [CheckIfBattleTagBelongsToAuthCode]
    public async Task<IActionResult> BlockRequest(string battleTag, [FromBody] string otherBattleTag)
    {
        try {
            var friendlist = await _friendRepository.LoadFriendlist(battleTag);
            CanBlock(friendlist, otherBattleTag);

            var request = await _friendRepository.LoadFriendRequest(otherBattleTag, battleTag);
            // var itemToRemove = friendlist.ReceivedFriendRequests.SingleOrDefault(bTag => bTag == otherBattleTag);
            if (request == null) {
                return BadRequest("Could not find a friend request to block.");
            }

            await _friendRepository.DeleteFriendRequest(request);

            friendlist.BlockedBattleTags.Add(otherBattleTag);
            await _friendRepository.UpsertFriendlist(friendlist);

            return Ok($"Friend requests from {otherBattleTag} blocked!");
        } catch (Exception ex) {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("{battleTag}/remove-friend")]
    [CheckIfBattleTagBelongsToAuthCode]
    public async Task<IActionResult> RemoveFriend(string battleTag, [FromBody] string otherBattleTag)
    {
        try {
            var currentUserFriendlist = await _friendRepository.LoadFriendlist(battleTag);
            var friendRelation1 = currentUserFriendlist.Friends.SingleOrDefault(bTag => bTag == otherBattleTag);
            if (friendRelation1 != null) {
                currentUserFriendlist.Friends.Remove(friendRelation1);
            }
            await _friendRepository.UpsertFriendlist(currentUserFriendlist);

            var otherUserFriendlist = await _friendRepository.LoadFriendlist(otherBattleTag);
            var friendRelation2 = otherUserFriendlist.Friends.SingleOrDefault(bTag => bTag == battleTag);
            if (friendRelation2 != null) {
                otherUserFriendlist.Friends.Remove(friendRelation2);
            }
            await _friendRepository.UpsertFriendlist(otherUserFriendlist);

            return Ok($"Removed {otherBattleTag} from friends.");
        } catch (Exception ex) {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("{battleTag}/unblock-request")]
    [CheckIfBattleTagBelongsToAuthCode]
    public async Task<IActionResult> UnblockRequest(string battleTag, [FromBody] string otherBattleTag)
    {
        try {
            var friendlist = await _friendRepository.LoadFriendlist(battleTag);
            var itemToRemove = friendlist.BlockedBattleTags.SingleOrDefault(bTag => bTag == otherBattleTag);
            if (itemToRemove != null) {
                friendlist.BlockedBattleTags.Remove(itemToRemove);
            }
            await _friendRepository.UpsertFriendlist(friendlist);

            return Ok($"Friend requests from {otherBattleTag} unblocked!");
        } catch (Exception ex) {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpDelete("{battleTag}/delete-request")]
    [CheckIfBattleTagBelongsToAuthCode]
    public async Task<IActionResult> DeleteOutgoingFriendRequest(string battleTag, [FromBody] string otherBattleTag)
    {
        try {
            var request = await _friendRepository.LoadFriendRequest(battleTag, otherBattleTag);
            if (request == null) {
                return BadRequest("Could not find a friend request to delete.");
            }

            await _friendRepository.DeleteFriendRequest(request);

            return Ok($"Friend request to {otherBattleTag} deleted!");
        } catch (Exception ex) {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpGet("{battleTag}/received-requests")]
    [CheckIfBattleTagBelongsToAuthCode]
    public async Task<IActionResult> LoadReceivedFriendRequests(string battleTag)
    {
        var requests = await _friendRepository.LoadAllFriendRequestsSentToPlayer(battleTag);
        return Ok(requests);
    }

    [HttpGet("{battleTag}/sent-requests")]
    [CheckIfBattleTagBelongsToAuthCode]
    public async Task<IActionResult> LoadSentFriendRequests(string battleTag)
    {
        var requests = await _friendRepository.LoadAllFriendRequestsSentByPlayer(battleTag);
        return Ok(requests);
    }

    [HttpGet("{battleTag}/friends")]
    [CheckIfBattleTagBelongsToAuthCode]
    public async Task<IActionResult> LoadFriends(string battleTag)
    {
        var friendlist = await _friendRepository.LoadFriendlist(battleTag);
        var battleTags = friendlist.Friends.ToArray();
        var personalSettings = await _personalSettingsRepository.LoadMany(battleTags);
        List<FriendDto> namesAndPictures = personalSettings.Select((x) =>
            new FriendDto {
                BattleTag = x.Id,
                ProfilePicture = x.ProfilePicture
            }
        ).ToList();
        return Ok(namesAndPictures);
    }

    private async Task CanMakeFriendRequest(Friendlist friendlist, FriendRequest req) {
        if (friendlist.BlockAllRequests || friendlist.BlockedBattleTags.Contains(req.Sender)) {
            throw new ValidationException("This player is not accepting friend requests.");
        }
        if (friendlist.Friends.Contains(req.Sender)) {
            throw new ValidationException("You are already friends with this player.");
        }

        var requestAlreadyExists = await _friendRepository.FriendRequestExists(req);
        if (requestAlreadyExists) {
            throw new ValidationException("You have already requested to be friends with this player.");
        }
    }

    private static void CanBlock(Friendlist friendlist, string battleTag) {
        if (friendlist.BlockedBattleTags.Contains(battleTag)) {
            throw new ValidationException("You have already blocked this player.");
        }
        if (friendlist.Friends.Contains(battleTag)) {
            throw new ValidationException("You cannot block a player you are friends with.");
        }
    }
}

public class FriendDto
{
    public string BattleTag { get; set; }
    public ProfilePicture ProfilePicture { get; set; }
}
