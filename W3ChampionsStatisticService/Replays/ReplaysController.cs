using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.Maps
{
    [ApiController]
    [Route("api/replays")]
    public class ReplaysController : ControllerBase
    {
        private readonly ReplayServiceClient _replayServiceClient;
        private readonly MatchRepository _matchRepository;
        public ReplaysController(
            ReplayServiceClient replayServiceClient,
            MatchRepository matchRepository)
        {
            _replayServiceClient = replayServiceClient;
            _matchRepository = matchRepository;
        }

        [HttpGet("{gameId}")]
        public async Task<IActionResult> GetReplay(string gameId)
        {
            var floMatchId = await _matchRepository.GetFloIdFromId(gameId);
            if (floMatchId == 0)
            {
                return NotFound();
            }
            var replayStream = await _replayServiceClient.GenerateReplay(floMatchId);
            return File(replayStream, "application/octet-stream", $"{gameId}.w3g");
        }
    }
}
