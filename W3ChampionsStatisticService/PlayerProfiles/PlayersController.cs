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
        private readonly IRankRepository _rankRepository;
        private readonly PlayerQueryHandler _playerQueryHandler;

        public PlayersController(
            IPlayerRepository playerRepository,
            IRankRepository rankRepository,
            PlayerQueryHandler playerQueryHandler)
        {
            _playerRepository = playerRepository;
            _rankRepository = rankRepository;
            _playerQueryHandler = playerQueryHandler;
        }

        [HttpGet("{battleTag}")]
        public async Task<IActionResult> GetPlayer([FromRoute] string battleTag, [FromRoute] int season)
        {
            var player = await _playerQueryHandler.LoadPlayerWithRanks(battleTag, season);
            return Ok(player);
        }

        [HttpGet("{battleTag}/winrate")]
        public async Task<IActionResult> GetPlayerWinrate([FromRoute] string battleTag, [FromRoute] int season)
        {
            var wins = await _playerRepository.LoadPlayerWinrate(battleTag, season);
            return Ok(wins);
        }
    }
}