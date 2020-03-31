using System;
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
        public async Task<IActionResult> GetMatches(DateTimeOffset lastObjectId = default, int pageSize = 100)
        {
            var matches = await _matchRepository.Load(lastObjectId, pageSize);
            return Ok(matches);
        }
    }
}