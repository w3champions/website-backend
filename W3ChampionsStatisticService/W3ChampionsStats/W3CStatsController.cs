using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.W3ChampionsStats.HeroWinrate;

namespace W3ChampionsStatisticService.W3ChampionsStats
{
    [ApiController]
    [Route("api/w3c-stats")]
    public class W3CStatsController : ControllerBase
    {
        private readonly IW3StatsRepo _w3StatsRepo;
        private readonly HeroStatsQueryHandler _heroStatsQueryHandler;

        public W3CStatsController(IW3StatsRepo w3StatsRepo, HeroStatsQueryHandler heroStatsQueryHandler)
        {
            _w3StatsRepo = w3StatsRepo;
            _heroStatsQueryHandler = heroStatsQueryHandler;
        }

        [HttpGet("map-race-wins")]
        public async Task<IActionResult> GetRaceVersusRaceStat()
        {
            var stats = await _w3StatsRepo.LoadRaceVsRaceStats();
            return Ok(stats);
        }

        [HttpGet("games-per-day")]
        public async Task<IActionResult> GetGamesPerDay(DateTimeOffset from = default, DateTimeOffset to = default)
        {
            from = from != default ? from : DateTimeOffset.MinValue;
            to = to != default ? to : DateTimeOffset.MaxValue;
            var gameDays = await _w3StatsRepo.LoadGamesPerDayBetween(from, to);
            return Ok(gameDays);
        }

        [HttpGet("games-lengths")]
        public async Task<IActionResult> GetGameLengths()
        {
            var stats = await _w3StatsRepo.LoadGameLengths();
            return Ok(stats);
        }

        [HttpGet("play-hours")]
        public async Task<IActionResult> GetPlayHours()
        {
            var stats = await _w3StatsRepo.LoadHourOfPlay();
            return Ok(stats.PlayTimesPerMode);
        }

        [HttpGet("heroes-played")]
        public async Task<IActionResult> GetPlayedHeroes()
        {
            var stats = await _w3StatsRepo.LoadHeroPlayedStat();
            return Ok(stats.Stats);
        }

        [HttpGet("heroes-winrate")]
        public async Task<IActionResult> GetHeroWinrate(
            string first,
            string second = "all",
            string third = "all",
            string opFirst = "all",
            string opSecond = "all",
            string opThird = "all")
        {
            var stats = await _heroStatsQueryHandler.GetStats(first, second, third, opFirst, opSecond, opThird);
            return Ok(stats);
        }

        [HttpGet("distinct-players-per-day")]
        public async Task<IActionResult> DistinctPlayersPerDay(DateTimeOffset from = default, DateTimeOffset to = default)
        {
            from = from != default ? from : DateTimeOffset.MinValue;
            to = to != default ? to : DateTimeOffset.MaxValue;
            var stats = await _w3StatsRepo.LoadPlayersPerDayBetween(from, to);
            return Ok(stats);
        }
    }
}