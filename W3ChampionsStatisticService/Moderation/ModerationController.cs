using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using System.Net;
using W3C.Contracts.Admin.Moderation;

namespace W3ChampionsStatisticService.Moderation
{
    [ApiController]
    [Route("api/moderation")]
    public class ModerationController : ControllerBase
    {
        private readonly MatchmakingServiceClient _matchmakingServiceRepository;

        public ModerationController(
            MatchmakingServiceClient matchmakingServiceRepository)
        {
            _matchmakingServiceRepository = matchmakingServiceRepository;
        }

        [HttpGet("loungeMutes")]
        public async Task<IActionResult> GetLoungeMutes()
        {
            var loungeMutes = await _matchmakingServiceRepository.GetLoungeMutes();
            return Ok(loungeMutes);
        }

        [HttpPost("loungeMutes")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> PostLoungeMute([FromBody] LoungeMute loungeMute)
        {
            if (loungeMute.battleTag == "") {
                return BadRequest("BattleTag cannot be empty.");
            }

            if (loungeMute.endDate == "") {
                return BadRequest("Ban End Date must be set.");
            }

            var result = await _matchmakingServiceRepository.PostLoungeMute(loungeMute);
            if (result.StatusCode == HttpStatusCode.Forbidden) {
                return StatusCode(403);
            }
            if (result.StatusCode == HttpStatusCode.BadRequest) {
                var reason = result.Content.ReadAsStringAsync().Result;
                return BadRequest(reason);
            }
            if (result.StatusCode == HttpStatusCode.OK) {
                return Ok();
            }
            return StatusCode(500);
        }

        [HttpDelete("loungeMutes/{bTag}")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> DeleteLoungeMute([FromRoute] string bTag)
        {
            var result = await _matchmakingServiceRepository.DeleteLoungeMute(bTag);
            if (result.StatusCode == HttpStatusCode.BadRequest) {
                var reason = result.Content.ReadAsStringAsync().Result;
                return BadRequest(reason);
            }
            if (result.StatusCode == HttpStatusCode.Forbidden) {
                return StatusCode(403);
            }
            if (result.StatusCode == HttpStatusCode.NotFound) {
                var reason = result.Content.ReadAsStringAsync().Result;
                return NotFound(reason);
            }
            if (result.StatusCode == HttpStatusCode.OK) {
                return Ok();
            }
            return StatusCode(500);
        }
    }
}
