using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Friends
{
    [ApiController]
    [Route("api/friends")]
    public class FriendsController : ControllerBase
    {
        private readonly IFriendRepository _friendRepository;

        public FriendsController(IFriendRepository friendRepository)
        {
            _friendRepository = friendRepository;
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
        public async Task<IActionResult> MakeFriendRequest(string battleTag, string target)
        {
            var friendlist = await _friendRepository.LoadFriendlist(target);
            if (!canMakeFriendRequest(friendlist, battleTag)) {
                return BadRequest();
            }
            friendlist.ReceivedFriendRequests.Add(battleTag);
            await _friendRepository.UpsertFriendlist(friendlist);

            return Ok();
        }

        [HttpPost("{battleTag}/accept-request")]
        [CheckIfBattleTagBelongsToAuthCode]
        public async Task<IActionResult> AcceptFriendRequest(string battleTag, string target)
        {
            var friendlist = await _friendRepository.LoadFriendlist(battleTag);
            var itemToRemove = friendlist.ReceivedFriendRequests.SingleOrDefault(r => r == target);

            if (itemToRemove == null) {
                return BadRequest();
            }

            friendlist.ReceivedFriendRequests.Remove(itemToRemove);
            friendlist.Friends.Add(target);
            await _friendRepository.UpsertFriendlist(friendlist);

            return Ok();
        }

        [HttpPost("{battleTag}/deny-request")]
        [CheckIfBattleTagBelongsToAuthCode]
        public async Task<IActionResult> DenyFriendRequest(string battleTag, string target)
        {
            var friendlist = await _friendRepository.LoadFriendlist(battleTag);
            var itemToRemove = friendlist.ReceivedFriendRequests.SingleOrDefault(r => r == target);

            if (itemToRemove == null) {
                return BadRequest();
            }

            friendlist.ReceivedFriendRequests.Remove(itemToRemove);
            await _friendRepository.UpsertFriendlist(friendlist);

            return Ok();
        }

        [HttpPost("{battleTag}/block")]
        [CheckIfBattleTagBelongsToAuthCode]
        public async Task<IActionResult> BlockRequest(string battleTag, string target)
        {
            var friendlist = await _friendRepository.LoadFriendlist(battleTag);
            if (!canBlock(friendlist, target)) {
                return BadRequest();
            }

            var itemToRemove = friendlist.ReceivedFriendRequests.SingleOrDefault(r => r == target);
            if (itemToRemove != null) {
                friendlist.ReceivedFriendRequests.Remove(itemToRemove);
            }

            friendlist.BlockedBattleTags.Add(target);
            await _friendRepository.UpsertFriendlist(friendlist);

            return Ok();
        }

        [HttpPost("{battleTag}/remove-friend")]
        [CheckIfBattleTagBelongsToAuthCode]
        public async Task<IActionResult> RemoveFriend(string battleTag, string target)
        {
            var friendlist = await _friendRepository.LoadFriendlist(battleTag);
            var itemToRemove = friendlist.Friends.SingleOrDefault(r => r == target);
            if (itemToRemove != null) {
                friendlist.Friends.Remove(itemToRemove);
            }
            await _friendRepository.UpsertFriendlist(friendlist);

            return Ok();
        }

        private bool canMakeFriendRequest(Friendlist friendlist, string battleTag) {
            if (friendlist.BlockAllRequests ||
                friendlist.BlockedBattleTags.Contains(battleTag) ||
                friendlist.Friends.Contains(battleTag) ||
                friendlist.ReceivedFriendRequests.Contains(battleTag)) {
                return false;
            }
            return true;
        }

        private bool canBlock(Friendlist friendlist, string battleTag) {
            if (friendlist.BlockedBattleTags.Contains(battleTag) || friendlist.Friends.Contains(battleTag)) {
                return false;
            }
            return true;
        }
    }
}
