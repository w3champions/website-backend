using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.W3ChampionsStats
{
    [ApiController]
    [Route("api/w3c-stats")]
    public class W3CStatsController : ControllerBase
    {
        private readonly IW3StatsRepo _w3StatsRepo;

        public W3CStatsController(IW3StatsRepo w3StatsRepo)
        {
            _w3StatsRepo = w3StatsRepo;
        }

        [HttpGet("map-race-wins")]
        public async Task<IActionResult> GetRaceVersusRaceStat()
        {
            var stats = await _w3StatsRepo.Load();
            return Ok(stats);
        }

        [HttpGet("games-per-day")]
        public async Task<IActionResult> GetGamesPerDay()
        {
            var stats = await _w3StatsRepo.LoadGamesPerDay();
            return Ok(stats);
        }
    }
}