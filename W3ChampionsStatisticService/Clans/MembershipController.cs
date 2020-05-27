using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace W3ChampionsStatisticService.Clans
{
    [ApiController]
    [Route("api/clans-memberships")]
    public class MembershipController : ControllerBase
    {
        private readonly ClanCommandHandler _clanCommandHandler;

        public MembershipController(
            ClanCommandHandler clanCommandHandler)
        {
            _clanCommandHandler = clanCommandHandler;
        }

        [HttpPut("{battleTag}")]
        public async Task<IActionResult> InviteToClan(
            [FromRoute] string battleTag,
            [FromBody] InviteToClanDto clanDto,
            string authorization)
        {
            // TODO auth
            await _clanCommandHandler.InviteToClan(battleTag, clanDto.ClanId, authorization);
            return Ok();
        }

    }
}