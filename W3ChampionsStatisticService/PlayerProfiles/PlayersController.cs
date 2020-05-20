using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    [ApiController]
    [Route("api/players")]
    public class PlayersController : ControllerBase
    {
        private readonly IPlayerRepository _playerRepository;

        public PlayersController(
            IPlayerRepository playerRepository)
        {
            _playerRepository = playerRepository;
        }

        [HttpGet("{battleTag}")]
        public async Task<IActionResult> GetPlayer([FromRoute] string battleTag)
        {
            var player = await _playerRepository.LoadPlayerProfile(battleTag);
            return Ok(player);
        }

        [HttpGet("{battleTag}/winrate")]
        public async Task<IActionResult> GetPlayerWinrate([FromRoute] string battleTag, int season)
        {
            var wins = await _playerRepository.LoadPlayerWinrate(battleTag, season);
            return Ok(wins);
        }

        [HttpGet("{battleTag}/game-mode-stats")]
        public async Task<IActionResult> GetGameModeStats(
            [FromRoute] string battleTag,
            GateWay gateWay,
            int season)
        {
            var wins = await _playerRepository.LoadGameModeStatPerGateway(battleTag, gateWay, season);
            return Ok(wins);
        }

        [HttpGet("{battleTag}/race-stats")]
        public async Task<IActionResult> GetRaceStats(
            [FromRoute] string battleTag,
            GateWay gateWay,
            int season)
        {
            var wins = await _playerRepository.LoadRaceStatPerGateway(battleTag, gateWay, season);
            var ordered = wins.OrderBy(s => s.Race).ToList();
            var firstPick = ordered[0];
            if (firstPick.Race != Race.RnD) return Ok(ordered);

            ordered.Remove(firstPick);
            ordered.Add(firstPick);
            return Ok(ordered);
        }
    }
}