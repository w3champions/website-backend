using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerOverviews
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
        public async Task<IActionResult> GetLadder(int mmr, int count, int gateWay)
        {
            var matches = await _playerRepository.LoadOverviewSince(mmr, count, gateWay);
            return Ok(matches);
        }
    }
}