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

        public LadderController(
            IPlayerRepository playerRepository,
            IRankeRepository rankeRepository)
        {
            _playerRepository = playerRepository;
            _rankeRepository = rankeRepository;
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchPlayer(string searchFor, int gateWay = 20)
        {
            var players = await _playerRepository.LoadOverviewLike(searchFor, gateWay);
            return Ok(players);
        }

        [HttpGet("{ladderId}")]
        public async Task<IActionResult> GetLadder([FromRoute] int leagueId, int gateWay = 20)
        {
            var playersInLadder = await _rankeRepository.LoadPlayerOfLeague(leagueId, gateWay);
            if (playersInLadder == null)
            {
                return NoContent();
            }

            return Ok(playersInLadder);
        }
    }
}