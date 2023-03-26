using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Domain.ChatService;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using System.Net;
using W3C.Contracts.Admin.Moderation;

namespace W3ChampionsStatisticService.Moderation
{
    [ApiController]
    [Route("api/moderation")]
    public class ModerationController : ControllerBase
    {
        private readonly ChatServiceClient _chatServiceRepository;

        public ModerationController(
            ChatServiceClient chatServiceRepository)
        {
            _chatServiceRepository = chatServiceRepository;
        }

        [HttpGet("loungeMute")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> GetLoungeMutes(string authorization)
        {
            var loungeMutes = await _chatServiceRepository.GetLoungeMutes(authorization);
            return Ok(loungeMutes);
        }

        [HttpPost("loungeMute")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> PostLoungeMute([FromBody] LoungeMute loungeMute, string authorization)
        {
            if (loungeMute.battleTag == "") {
                return BadRequest("BattleTag cannot be empty.");
            }

            if (loungeMute.endDate == "") {
                return BadRequest("Ban End Date must be set.");
            }

            var result = await _chatServiceRepository.PostLoungeMute(loungeMute, authorization);
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

        [HttpDelete("loungeMute/{bTag}")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> DeleteLoungeMute([FromRoute] string bTag, string authorization)
        {
            var result = await _chatServiceRepository.DeleteLoungeMute(bTag, authorization);
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
