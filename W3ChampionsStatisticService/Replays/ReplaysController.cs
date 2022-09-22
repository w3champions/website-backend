using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using W3C.Domain.UpdateService;

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

        [HttpGet("{gameId}")]
        public async Task<IActionResult> GetReplay(int gameId)
        {
            var replayStream = await _replayServiceClient.GenerateReplay(gameId);
            return File(replayStream, "application/octet-stream", $"{gameId}.w3g");
        }
    }
}
