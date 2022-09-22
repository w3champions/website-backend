using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.WebApiCompatShim;
using System.Net.Http;
using System.Threading.Tasks;
using W3C.Domain.MatchmakingService;
using W3C.Domain.MatchmakingService.MatchmakingContracts;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Maps
{
    [ApiController]
    [Route("api/replays")]
    public class ReplaysController : ControllerBase
    {
        ReplayServiceClient _replayServiceClient;
        public ReplaysController(ReplayServiceClient replayServiceClient)
        {
            _replayServiceClient = replayServiceClient;
        }

        [HttpGet("{id}")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> GetReplay([FromRoute] int gameId)
        {
            var replayStream = await _replayServiceClient.GenerateReplay(gameId);
            if (replayStream == null) return NotFound();
            var file = File(replayStream, "application/octet-stream");
            file.FileDownloadName = $"{gameId}.w3g";
            return Ok(file);
        }
    }
}
