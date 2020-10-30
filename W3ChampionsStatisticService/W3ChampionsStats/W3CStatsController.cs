using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.W3ChampionsStats.HeroWinrate;
using W3ChampionsStatisticService.W3ChampionsStats.MmrDistribution;

namespace W3ChampionsStatisticService.W3ChampionsStats
{
    [ApiController]
    [Route("api/w3c-stats")]
    public class W3CStatsController : ControllerBase
    {
        private readonly IW3StatsRepo _w3StatsRepo;
        private readonly HeroStatsQueryHandler _heroStatsQueryHandler;
        private readonly MmrDistributionHandler _mmrDistributionHandler;

        public W3CStatsController(
            IW3StatsRepo w3StatsRepo,
            HeroStatsQueryHandler heroStatsQueryHandler,
            MmrDistributionHandler mmrDistributionHandler)
        {
            _w3StatsRepo = w3StatsRepo;
            _heroStatsQueryHandler = heroStatsQueryHandler;
            _mmrDistributionHandler = mmrDistributionHandler;
        }

        [HttpGet("map-race-wins")]
        public async Task<IActionResult> GetRaceVersusRaceStat()
        {
            var stats = await _w3StatsRepo.LoadRaceVsRaceStats();
            return Ok(stats);
        }

        [HttpGet("games-per-day")]
        public async Task<IActionResult> GetGamesPerDay(
            DateTimeOffset from = default,
            DateTimeOffset to = default)
        {
            from = from != default ? from : GetDefaultMinDateOffset();
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
            from = from != default ? from : GetDefaultMinDateOffset();
            to = to != default ? to : DateTimeOffset.MaxValue;
            var stats = await _w3StatsRepo.LoadPlayersPerDayBetween(from, to);
            return Ok(stats);
        }

        [HttpGet("mmr-distribution")]
        public async Task<IActionResult> GetMmrDistribution(int season, GateWay gateWay = GateWay.Europe, GameMode gameMode = GameMode.GM_1v1)
        {
            var mmrs = await _mmrDistributionHandler.GetDistributions(season, gateWay, gameMode);
            return Ok(mmrs);
        }

        [HttpGet("matches-on-map")]
        public async Task<IActionResult> GetMatchesOnMap()
        {
            var mmrs = await _w3StatsRepo.LoadMatchesOnMap();
            return Ok(mmrs);
        }

        private DateTimeOffset GetDefaultMinDateOffset()
        {
            DateTime ThreeMonthsInThePast = DateTime.UtcNow.AddMonths(-2);
            return DateTime.SpecifyKind(ThreeMonthsInThePast, DateTimeKind.Utc);
        }
    }
}