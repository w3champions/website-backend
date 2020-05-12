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

        [HttpGet("{battleTag}/race-on-map-versus-race")]
        public async Task<IActionResult> GetRaceOnMapVersusRaceStat([FromRoute] string battleTag, int season)
        {
            var matches = await _playerRepository.LoadMapAndRaceStat(battleTag, season);
            return Ok(matches);
        }

        [HttpGet("{battleTag}/hero-on-map-versus-race")]
        public async Task<IActionResult> GetHeroOnMapVersusRaceStat([FromRoute] string battleTag, int season)
        {
            var matches = await _playerRepository.LoadHeroStat(battleTag, season);
            return Ok(matches);
        }
    }
}