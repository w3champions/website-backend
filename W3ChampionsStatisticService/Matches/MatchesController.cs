using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Matches
{
    [ApiController]
    [Route("api/matches")]
    public class MatchesController : ControllerBase
    {
        private readonly IMatchRepository _matchRepository;

        public MatchesController(IMatchRepository matchRepository)
        {
            _matchRepository = matchRepository;
        }

        [HttpGet]
        public async Task<IActionResult> GetMatches(
            int offset = 0,
            int pageSize = 50,
            int gateWay = 10)
        {
            var matches = await _matchRepository.Load(offset, pageSize, gateWay);
            var count = await _matchRepository.Count();
            return Ok(new { matches, count });
        }

        [HttpGet("search")]
        public async Task<IActionResult> GetMatchesPerPlayer(
            string playerId,
            int offset = 0,
            int pageSize = 100)
        {
            var matches = await _matchRepository.LoadFor(playerId, pageSize, offset);
            var count = await _matchRepository.CountFor(playerId);
            return Ok(new { matches, count });
        }
    }
}