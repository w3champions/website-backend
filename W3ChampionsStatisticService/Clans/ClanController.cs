using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Clans.Commands;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Clans
{
    [ApiController]
    [Route("api/clans")]
    public class ClanController : ControllerBase
    {
        private readonly ClanCommandHandler _clanCommandHandler;
        private readonly IClanRepository _clanRepository;

        public ClanController(
            ClanCommandHandler clanCommandHandler,
            IClanRepository clanRepository)
        {
            _clanCommandHandler = clanCommandHandler;
            _clanRepository = clanRepository;
        }

        [HttpPost]
        [InjectActingPlayerAuthCode]
        public async Task<IActionResult> CreateClan(
            [FromBody] CreateClanDto clanDto,
            string actingPlayer)
        {
            var clan = await _clanCommandHandler.CreateClan(clanDto.ClanName, actingPlayer);
            return Ok(clan);
        }

        [HttpGet("{clanId}")]
        public async Task<IActionResult> GetClan(string clanId)
        {
            var clan = await _clanRepository.LoadClan(clanId);
            return Ok(clan);
        }

        [HttpGet]
        public async Task<IActionResult> GetClanForPlayer(string battleTag)
        {
            var clan = await _clanCommandHandler.GetClanForPlayer(battleTag);
            return Ok(clan);
        }

        [HttpDelete("{clanId}")]
        [InjectActingPlayerAuthCode]
        public async Task<IActionResult> DeleteClan(
            string clanId,
            string actingPlayer)
        {
            await _clanCommandHandler.DeleteClan(clanId, actingPlayer);
            return Ok();
        }

        [HttpPost("{clanId}/invites")]
        [InjectActingPlayerAuthCode]
        public async Task<IActionResult> InviteToClan(
            string clanId,
            string actingPlayer,
            [FromBody] InviteDto inviteDto)
        {
            await _clanCommandHandler.InviteToClan(clanId, inviteDto.PlayerBattleTag, actingPlayer);
            return Ok();
        }

        [HttpDelete("{clanId}/invites")]
        [InjectActingPlayerAuthCode]
        public async Task<IActionResult> RevokeInvitationToClan(
            string clanId,
            string actingPlayer,
            [FromBody] InviteDto inviteDto)
        {
            await _clanCommandHandler.RevokeInvitationToClan(inviteDto.PlayerBattleTag, clanId, actingPlayer);
            return Ok();
        }

        [HttpPost("{clanId}/shamans")]
        [InjectActingPlayerAuthCode]
        public async Task<IActionResult> AddShamanToClan(
            string clanId,
            string actingPlayer,
            [FromBody] InviteDto inviteDto)
        {
            var clan = await _clanCommandHandler.AddShamanToClan(inviteDto.PlayerBattleTag, clanId, actingPlayer);
            return Ok(clan);
        }

        [HttpDelete("{clanId}/shamans/{shamanId}")]
        [InjectActingPlayerAuthCode]
        public async Task<IActionResult> RemoveShamanFromClan(
            string clanId,
            string actingPlayer,
            string shamanId)
        {
            var clan = await _clanCommandHandler.RemoveShamanFromClan(shamanId, clanId, actingPlayer);
            return Ok(clan);
        }

        [HttpDelete("{clanId}/members/{battleTag}")]
        [CheckIfBattleTagBelongsToAuthCode]
        public async Task<IActionResult> RevokeInvitationToClan(
            string clanId,
            string battleTag)
        {
            var clan = await _clanCommandHandler.LeaveClan(clanId, battleTag);
            return Ok(clan);
        }

        [HttpPut("{clanId}/invites/{battleTag}")]
        [CheckIfBattleTagBelongsToAuthCode]
        public async Task<IActionResult> AcceptInvite(string clanId, string battleTag)
        {
            var clan = await _clanCommandHandler.AcceptInvite(clanId, battleTag);
            return Ok(clan);
        }

        [HttpDelete("{clanId}/invites/{battleTag}")]
        [CheckIfBattleTagBelongsToAuthCode]
        public async Task<IActionResult> RejectInvite(string clanId, string battleTag)
        {
            var clan = await _clanCommandHandler.RejectInvite(clanId, battleTag);
            return Ok(clan);
        }
    }
}