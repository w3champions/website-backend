using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Ladder
{
    [ApiController]
    [Route("api/ladder")]
    public class LadderController : ControllerBase
    {
        private readonly IPlayerRepository _playerRepository;
        private readonly IRankeRepository _rankeRepository;
        private readonly IMatchEventRepository _matchEventRepository;

        public LadderController(
            IPlayerRepository playerRepository,
            IRankeRepository rankeRepository,
            IMatchEventRepository matchEventRepository)
        {
            _playerRepository = playerRepository;
            _rankeRepository = rankeRepository;
            _matchEventRepository = matchEventRepository;
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchPlayer(string searchFor, int gateWay = 20)
        {
            var players = await _playerRepository.LoadOverviewLike(searchFor, gateWay);
            return Ok(players);
        }

        [HttpGet("{leagueId}")]
        public async Task<IActionResult> GetLadder([FromRoute] int leagueId, int gateWay = 20)
        {
            var playersInLadder = await _rankeRepository.LoadPlayerOfLeague(leagueId, gateWay);
            if (playersInLadder == null)
            {
                return NoContent();
            }

            return Ok(playersInLadder);
        }

        [HttpGet("league-constellation")]
        public async Task<IActionResult> GetLeagueConstellation()
        {
            var leagues = await _matchEventRepository.LoadLeagueConstellation();
            return Ok(leagues);
        }
    }
}