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

        public LadderController(IPlayerRepository playerRepository)
        {
            _playerRepository = playerRepository;
        }

        [HttpGet]
        public async Task<IActionResult> GetLadder(int offset, int pageSize, int gateWay)
        {
            var matches = await _playerRepository.LoadOverviewSince(offset, pageSize, gateWay);
            return Ok(matches);
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchPlayer(string searchFor, int gateWay = 20)
        {
            var players = await _playerRepository.LoadOverviewLike(searchFor, gateWay);
            return Ok(players);
        }
    }
}