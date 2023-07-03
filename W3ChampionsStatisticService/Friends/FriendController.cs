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
                    return Ok($"Player {data.otherBattleTag} not found.");
                }
                if (battleTag.ToLower() == data.otherBattleTag.ToLower()) {
                    return Ok($"Cannot request yourself as a friend.");
                }
                var friendlist = await _friendRepository.LoadFriendlist(data.otherBattleTag);
                canMakeFriendRequest(friendlist, battleTag);
                friendlist.ReceivedFriendRequests.Add(battleTag);
                await _friendRepository.UpsertFriendlist(friendlist);

                return Ok($"Friend request sent to {data.otherBattleTag}!");
            } catch (ValidationException ex) {
                return Ok(ex.Message);
            } catch (Exception ex) {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("{battleTag}/accept-request")]
        [CheckIfBattleTagBelongsToAuthCode]
        public async Task<IActionResult> AcceptFriendRequest(string battleTag, [FromBody] FriendData data)
        {
            try {
                var friendlist = await _friendRepository.LoadFriendlist(battleTag);
                var itemToRemove = friendlist.ReceivedFriendRequests.SingleOrDefault(bTag => bTag == data.otherBattleTag);

                if (itemToRemove == null) {
                    return BadRequest("Could not find a friend request to accept.");
                }

                friendlist.ReceivedFriendRequests.Remove(itemToRemove);
                friendlist.Friends.Add(data.otherBattleTag);
                await _friendRepository.UpsertFriendlist(friendlist);
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
                var friendlist = await _friendRepository.LoadFriendlist(battleTag);
                var itemToRemove = friendlist.ReceivedFriendRequests.SingleOrDefault(bTag => bTag == data.otherBattleTag);

                if (itemToRemove == null) {
                    return BadRequest("Could not find a friend request to deny.");
                }

                friendlist.ReceivedFriendRequests.Remove(itemToRemove);
                await _friendRepository.UpsertFriendlist(friendlist);

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

                var itemToRemove = friendlist.ReceivedFriendRequests.SingleOrDefault(bTag => bTag == data.otherBattleTag);
                if (itemToRemove != null) {
                    friendlist.ReceivedFriendRequests.Remove(itemToRemove);
                }

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
                var friendlist = await _friendRepository.LoadFriendlist(battleTag);
                var itemToRemove = friendlist.Friends.SingleOrDefault(bTag => bTag == data.otherBattleTag);
                if (itemToRemove != null) {
                    friendlist.Friends.Remove(itemToRemove);
                }
                await _friendRepository.UpsertFriendlist(friendlist);

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

        private void canMakeFriendRequest(Friendlist friendlist, string battleTag) {
            if (friendlist.BlockAllRequests || friendlist.BlockedBattleTags.Contains(battleTag)) {
                throw new ValidationException("This player is not accepting friend requests.");
            }
            if (friendlist.ReceivedFriendRequests.Contains(battleTag)) {
                throw new ValidationException("You have already requested to be friends with this player.");
            }
            if (friendlist.Friends.Contains(battleTag)) {
                throw new ValidationException("You are already friends with this player.");
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
