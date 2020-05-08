using System;
using System.Collections.Generic;
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

        public W3CStatsController(IW3StatsRepo w3StatsRepo)
        {
            _w3StatsRepo = w3StatsRepo;
        }

        [HttpGet("map-race-wins")]
        public async Task<IActionResult> GetRaceVersusRaceStat()
        {
            var stats = await _w3StatsRepo.Load();
            return Ok(stats.StatsPerModes);
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
            string searchString = first;
            if (second == "none" || third == "none")
            {
                if (second != "none") searchString += $"_{second}";
                if (third != "none") searchString += $"_{third}";
                var stats = await _w3StatsRepo.LoadHeroWinrate(searchString);
                var heroWinrateDto = new HeroWinrateDto(new List<HeroWinRatePerHero> { stats }, opFirst, opSecond, opThird);
                return Ok(heroWinrateDto);
            }
            else
            {
                if (second != "all") searchString += $"_{second}";
                if (third != "all") searchString += $"_{third}";
                var stats = await _w3StatsRepo.LoadHeroWinrateLike(searchString);
                var heroWinrateDto = new HeroWinrateDto(stats, opFirst, opSecond, opThird);
                return Ok(heroWinrateDto);
            }
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