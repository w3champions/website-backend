using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerStats
{
    [ApiController]
    [Route("api/player-stats")]
    public class PlayerStatsController : ControllerBase
    {
        private readonly IPlayerStatsRepository _playerRepository;

        public PlayerStatsController(IPlayerStatsRepository playerRepository)
        {
            _playerRepository = playerRepository;
        }

        [HttpGet("{battleTag}/race-versus-race")]
        public async Task<IActionResult> GetRaceVersusRaceStat([FromRoute] string battleTag)
        {
            var matches = await _playerRepository.LoadRaceStat(battleTag);
            return Ok(matches);
        }

        [HttpGet("{battleTag}/race-on-map")]
        public async Task<IActionResult> GetRaceOnMapStat([FromRoute] string battleTag)
        {
            var matches = await _playerRepository.LoadMapStat(battleTag);
            return Ok(matches);
        }

        [HttpGet("{battleTag}/race-on-map-versus-race")]
        public async Task<IActionResult> GetRaceOnMapVersusRaceStat([FromRoute] string battleTag)
        {
            var matches = await _playerRepository.LoadMapAndRaceStat(battleTag);
            return Ok(matches);
        }
    }
}