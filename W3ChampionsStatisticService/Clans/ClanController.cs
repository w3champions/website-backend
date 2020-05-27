using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Clans.Commands;

namespace W3ChampionsStatisticService.Clans
{
    [ApiController]
    [Route("api/clans")]
    public class ClanController : ControllerBase
    {
        private readonly ClanCommandHandler _clanCommandHandler;

        public ClanController(
            ClanCommandHandler clanCommandHandler)
        {
            _clanCommandHandler = clanCommandHandler;
        }

        [HttpPost]
        public async Task<IActionResult> CreateClan([FromBody] CreateClanDto clanDto, string authorization)
        {
            var clan = await _clanCommandHandler.CreateClan(clanDto.ClanName, "123");
            return Ok(clan);
        }

        [HttpPost("{clanId}/signees")]
        public async Task<IActionResult> SignClanPetition(string clanId, [FromBody] SignClanDto clanDto)
        {
            var clan = await _clanCommandHandler.SignClanPetition(clanId, clanDto.PlayerBattleTag);
            return Ok(clan);
        }

        [HttpPost("{clanId}/members")]
        public async Task<IActionResult> AddMember(string clanId, [FromBody] AddMemberDto clanDto)
        {
            var clan = await _clanCommandHandler.AddMember(clanId, clanDto.PlayerBattleTag);
            return Ok(clan);
        }
    }
}