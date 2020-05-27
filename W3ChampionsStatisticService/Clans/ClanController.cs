using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Clans.Commands;
using W3ChampionsStatisticService.Ports;

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
        public async Task<IActionResult> CreateClan([FromBody] CreateClanDto clanDto)
        {
            var clan = await _clanCommandHandler.CreateClan(clanDto.ClanName, "123");
            return Ok(clan);
        }

        [HttpPost("{clanId}/signees")]
        public async Task<IActionResult> SignClanPetition(string clanId, [FromBody] SignClanDto clanDto)
        {
            var clan = await _clanCommandHandler.SignClanPetition(clanDto.PlayerBattleTag, clanId);
            return Ok(clan);
        }

        [HttpGet("{clanId}")]
        public async Task<IActionResult> GetClan(string clanId)
        {
            var clan = await _clanRepository.LoadClan(clanId);
            return Ok(clan);
        }

        [HttpDelete("{clanId}")]
        public async Task<IActionResult> DeleteClan(string clanId)
        {
            await _clanCommandHandler.DeleteClan(clanId);
            return Ok();
        }

        [HttpPost("{clanId}/invites")]
        public async Task<IActionResult> InviteToClan(string clanId, [FromBody] CreateInviteDto inviteDto)
        {
            await _clanCommandHandler.InviteToClan(inviteDto.PlayerBattleTag, clanId, "123");
            return Ok();
        }

        [HttpPut("{clanId}/invites/{battleTag}")]
        public async Task<IActionResult> AcceptInvite(string clanId, string battleTag)
        {
            var clan = await _clanCommandHandler.AcceptInvite(clanId, battleTag);
            return Ok(clan);
        }
    }
}