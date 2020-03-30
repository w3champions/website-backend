using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    [ApiController]
    [Route("api/players")]
    public class PlayersController : ControllerBase
    {
        private readonly IPlayerRepository _playerRepository;

        public PlayersController(IPlayerRepository playerRepository)
        {
            _playerRepository = playerRepository;
        }

        [HttpGet("{battleTag}")]
        public async Task<IActionResult> GetMatches([FromRoute] string battleTag)
        {
            var matches = await _playerRepository.Load(battleTag);
            return Ok(matches);
        }
    }
}