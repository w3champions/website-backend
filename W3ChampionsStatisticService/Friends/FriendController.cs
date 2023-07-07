using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using W3ChampionsStatisticService.Ports;
using System.ComponentModel.DataAnnotations;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Friends
{
    [ApiController]
    [Route("api/friends")]
    public class FriendsController : ControllerBase
    {
        private readonly IFriendRepository _friendRepository;
        private readonly IPlayerRepository _playerRepository;

        public FriendsController(
            IFriendRepository friendRepository,
            IPlayerRepository playerRepository
        )
        {
            _friendRepository = friendRepository;
            _playerRepository = playerRepository;
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
        public async Task<IActionResult> MakeFriendRequest(string battleTag, [FromBody] FriendData data)
        {
            try {
                var player = await _playerRepository.LoadPlayerProfile(data.otherBattleTag);
                if (player == null) {
                    return BadRequest($"Player {data.otherBattleTag} not found.");
                }
                if (battleTag.ToLower() == data.otherBattleTag.ToLower()) {
                    return BadRequest($"Cannot request yourself as a friend.");
                }
                var allRequestsMadeByPlayer = await _friendRepository.LoadAllFriendRequestsSentByPlayer(battleTag);
                if (allRequestsMadeByPlayer.Count() > 10) {
                    return BadRequest($"You have too many pending friend requests.");
                }
                var request = new FriendRequest(battleTag, data.otherBattleTag);
                var otherUserFriendlist = await _friendRepository.LoadFriendlist(data.otherBattleTag);
                await canMakeFriendRequest(otherUserFriendlist, request);
                await _friendRepository.CreateFriendRequest(request);

                return Ok($"Friend request sent to {data.otherBattleTag}!");
            } catch (ValidationException ex) {
                return BadRequest(ex.Message);
            } catch (Exception ex) {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("{battleTag}/accept-request")]
        [CheckIfBattleTagBelongsToAuthCode]
        public async Task<IActionResult> AcceptFriendRequest(string battleTag, [FromBody] FriendData data)
        {
            try {
                var currentUserFriendlist = await _friendRepository.LoadFriendlist(battleTag);
                var otherUserFriendlist = await _friendRepository.LoadFriendlist(data.otherBattleTag);
                var request = await _friendRepository.LoadFriendRequest(data.otherBattleTag, battleTag);

                if (request == null) {
                    return BadRequest("Could not find a friend request to accept.");
                }

                await _friendRepository.DeleteFriendRequest(request);

                if (!currentUserFriendlist.Friends.Contains(data.otherBattleTag)) {
                    currentUserFriendlist.Friends.Add(data.otherBattleTag);
                }
                await _friendRepository.UpsertFriendlist(currentUserFriendlist);

                if (!otherUserFriendlist.Friends.Contains(battleTag)) {
                    otherUserFriendlist.Friends.Add(battleTag);
                }

                var reciprocalRequest = await _friendRepository.LoadFriendRequest(battleTag, data.otherBattleTag);
                if (reciprocalRequest != null) {
                    await _friendRepository.DeleteFriendRequest(reciprocalRequest);
                }

                await _friendRepository.UpsertFriendlist(otherUserFriendlist);
                return Ok($"Friend request from {data.otherBattleTag} accepted!");
            } catch (Exception ex) {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("{battleTag}/deny-request")]
        [CheckIfBattleTagBelongsToAuthCode]
        public async Task<IActionResult> DenyFriendRequest(string battleTag, [FromBody] FriendData data)
        {
            try {
                var request = await _friendRepository.LoadFriendRequest(data.otherBattleTag, battleTag);

                if (request == null) {
                    return BadRequest("Could not find a friend request to deny.");
                }

                await _friendRepository.DeleteFriendRequest(request);

                return Ok($"Friend request from {data.otherBattleTag} denied!");
            } catch (Exception ex) {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("{battleTag}/block-request")]
        [CheckIfBattleTagBelongsToAuthCode]
        public async Task<IActionResult> BlockRequest(string battleTag, [FromBody] FriendData data)
        {
            try {
                var friendlist = await _friendRepository.LoadFriendlist(battleTag);
                canBlock(friendlist, data.otherBattleTag);

                var request = await _friendRepository.LoadFriendRequest(data.otherBattleTag, battleTag);
                // var itemToRemove = friendlist.ReceivedFriendRequests.SingleOrDefault(bTag => bTag == data.otherBattleTag);
                if (request == null) {
                    return BadRequest("Could not find a friend request to block.");
                }

                await _friendRepository.DeleteFriendRequest(request);

                friendlist.BlockedBattleTags.Add(data.otherBattleTag);
                await _friendRepository.UpsertFriendlist(friendlist);

                return Ok($"Friend requests from {data.otherBattleTag} blocked!");
            } catch (Exception ex) {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("{battleTag}/remove-friend")]
        [CheckIfBattleTagBelongsToAuthCode]
        public async Task<IActionResult> RemoveFriend(string battleTag, [FromBody] FriendData data)
        {
            try {
                var currentUserFriendlist = await _friendRepository.LoadFriendlist(battleTag);
                var friendRelation1 = currentUserFriendlist.Friends.SingleOrDefault(bTag => bTag == data.otherBattleTag);
                if (friendRelation1 != null) {
                    currentUserFriendlist.Friends.Remove(friendRelation1);
                }
                await _friendRepository.UpsertFriendlist(currentUserFriendlist);

                var otherUserFriendlist = await _friendRepository.LoadFriendlist(data.otherBattleTag);
                var friendRelation2 = otherUserFriendlist.Friends.SingleOrDefault(bTag => bTag == battleTag);
                if (friendRelation2 != null) {
                    otherUserFriendlist.Friends.Remove(friendRelation2);
                }
                await _friendRepository.UpsertFriendlist(otherUserFriendlist);

                return Ok($"Removed {data.otherBattleTag} from friends.");
            } catch (Exception ex) {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("{battleTag}/unblock-request")]
        [CheckIfBattleTagBelongsToAuthCode]
        public async Task<IActionResult> UnblockRequest(string battleTag, [FromBody] FriendData data)
        {
            try {
                var friendlist = await _friendRepository.LoadFriendlist(battleTag);
                var itemToRemove = friendlist.BlockedBattleTags.SingleOrDefault(bTag => bTag == data.otherBattleTag);
                if (itemToRemove != null) {
                    friendlist.BlockedBattleTags.Remove(itemToRemove);
                }
                await _friendRepository.UpsertFriendlist(friendlist);

                return Ok($"Friend requests from {data.otherBattleTag} unblocked!");
            } catch (Exception ex) {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("{battleTag}/delete-request")]
        [CheckIfBattleTagBelongsToAuthCode]
        public async Task<IActionResult> DeleteOutgoingFriendRequest(string battleTag, [FromBody] FriendData data)
        {
            try {
                var request = await _friendRepository.LoadFriendRequest(battleTag, data.otherBattleTag);
                if (request == null) {
                    return BadRequest("Could not find a friend request to delete.");
                }

                await _friendRepository.DeleteFriendRequest(request);

                return Ok($"Friend request to {data.otherBattleTag} deleted!");
            } catch (Exception ex) {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("{battleTag}/received-requests")]
        [CheckIfBattleTagBelongsToAuthCode]
        public async Task<IActionResult> LoadReceivedFriendRequests(string battleTag, string sender)
        {
            var friendlist = await _friendRepository.LoadAllFriendRequestsSentToPlayer(battleTag);
            return Ok(friendlist);
        }

        [HttpGet("{battleTag}/sent-requests")]
        [CheckIfBattleTagBelongsToAuthCode]
        public async Task<IActionResult> LoadSentFriendRequests(string battleTag, string sender)
        {
            var friendlist = await _friendRepository.LoadAllFriendRequestsSentByPlayer(battleTag);
            return Ok(friendlist);
        }

        private async Task canMakeFriendRequest(Friendlist friendlist, FriendRequest req) {
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

        private void canBlock(Friendlist friendlist, string battleTag) {
            if (friendlist.BlockedBattleTags.Contains(battleTag)) {
                throw new ValidationException("You have already blocked this player.");
            }
            if (friendlist.Friends.Contains(battleTag)) {
                throw new ValidationException("You cannot block a player you are friends with.");
            }
        }
    }

    public class FriendData
    {
        public string otherBattleTag { get; set; }
    }
}
