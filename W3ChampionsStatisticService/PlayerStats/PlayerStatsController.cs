using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Services;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.PlayerStats;

[ApiController]
[Route("api/player-stats")]
[Trace]
public class PlayerStatsController(PlayerStatisticsService playerStatisticsService) : ControllerBase
{
    private readonly PlayerStatisticsService _playerStatisticsService = playerStatisticsService;

    [HttpGet("{battleTag}/race-on-map-versus-race")]
    public async Task<IActionResult> GetRaceOnMapVersusRaceStat([FromRoute] string battleTag, int season)
    {
        try
        {
            var stats = await _playerStatisticsService.GetMapAndRaceStatAsync(battleTag, season);
            return Ok(stats);
        }
        catch (HttpRequestException ex)
        {
            int statusCode = ex.StatusCode is null ? 500 : (int)ex.StatusCode;
            return StatusCode(statusCode, ex.Message);
        }
    }

    [HttpGet("{battleTag}/hero-on-map-versus-race")]
    public async Task<IActionResult> GetHeroOnMapVersusRaceStat([FromRoute] string battleTag, int season)
    {
        var stats = await _playerStatisticsService.GetHeroStatAsync(battleTag, season);
        return Ok(stats);
    }
}
