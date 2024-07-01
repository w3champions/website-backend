﻿using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.W3ChampionsStats.HeroWinrate;
using W3ChampionsStatisticService.W3ChampionsStats.MmrDistribution;
using System.Linq;
using W3C.Contracts.GameObjects;

namespace W3ChampionsStatisticService.W3ChampionsStats;

[ApiController]
[Route("api/w3c-stats")]
public class W3CStatsController : ControllerBase
{
    private readonly IW3StatsRepo _w3StatsRepo;
    private readonly HeroStatsQueryHandler _heroStatsQueryHandler;
    private readonly MmrDistributionHandler _mmrDistributionHandler;
    private readonly PlayerStatisticsService _statisticsService;
    private readonly W3StatsService _w3StatsService;

    public W3CStatsController(
        IW3StatsRepo w3StatsRepo,
        HeroStatsQueryHandler heroStatsQueryHandler,
        MmrDistributionHandler mmrDistributionHandler,
        PlayerStatisticsService statisticsService,
        W3StatsService w3StatsService
        )
    {
        _w3StatsRepo = w3StatsRepo;
        _heroStatsQueryHandler = heroStatsQueryHandler;
        _mmrDistributionHandler = mmrDistributionHandler;
        _statisticsService = statisticsService;
        _w3StatsService = w3StatsService;
    }

    [HttpGet("map-race-wins")]
    public async Task<IActionResult> GetRaceVersusRaceStat()
    {
        var stats = await _w3StatsRepo.LoadRaceVsRaceStats();
        return Ok(stats);
    }

    [HttpGet("map-race-recent-wins")]
    public async Task<IActionResult> GetRecentRaceVersusRaceStat(int n = 3)
    {
        var stats = await _w3StatsService.LoadNRaceVsRaceStats(n);
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
        var stats = await _w3StatsRepo.LoadAllGameLengths();
        return Ok(stats);
    }

    [HttpGet("matchup-lengths")]
    public async Task<IActionResult> GetMatchupLengths(Race race1, Race race2, string season)
    {
        var matchupLength = await _w3StatsRepo.LoadMatchupLengthOrCreate(race1.ToString(), race2.ToString(), season);
        return Ok(matchupLength);
    }

    [HttpGet("popular-hours")]
    public async Task<IActionResult> GetPopularHours()
    {
        var stats = await _w3StatsRepo.LoadAllPopularHoursStat();
        var totalStats = stats.Select(stat => new { stat.GameMode, stat.PopularHoursTotal.Timeslots });
        return Ok(totalStats);
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
        var mmrs = await _statisticsService.LoadMatchesOnMapAsync();
        return Ok(mmrs);
    }

    private DateTimeOffset GetDefaultMinDateOffset()
    {
        DateTime ThreeMonthsInThePast = DateTime.UtcNow.AddMonths(-2);
        return DateTime.SpecifyKind(ThreeMonthsInThePast, DateTimeKind.Utc);
    }
}
