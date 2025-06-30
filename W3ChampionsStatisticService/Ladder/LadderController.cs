using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;
using W3C.Domain.Tracing;
using System.Collections.Generic;

namespace W3ChampionsStatisticService.Ladder;

[ApiController]
[Route("api/ladder")]
[Trace]
public class LadderController(
    IRankRepository rankRepository,
    IPlayerRepository playerRepository,
    RankQueryHandler rankQueryHandler,
    PlayerAkaProvider playerAkaProvider,
    IMatchmakingProvider matchmakingProvider) : ControllerBase
{
    private readonly IRankRepository _rankRepository = rankRepository;
    private readonly IPlayerRepository _playerRepository = playerRepository;
    private readonly RankQueryHandler _rankQueryHandler = rankQueryHandler;
    private readonly PlayerAkaProvider _playerAkaProvider = playerAkaProvider;
    private readonly IMatchmakingProvider _matchmakingProvider = matchmakingProvider;

    [HttpGet("search")]
    public async Task<IActionResult> SearchPlayer(string searchFor, int season, GateWay gateWay = GateWay.Europe, GameMode gameMode = GameMode.GM_1v1)
    {
        if (string.IsNullOrEmpty(searchFor) || searchFor.Length < 3)
        {
            return BadRequest("searchFor parameter must be at least 3 letters.");
        }
        List<Rank> playerRanks = await _rankRepository.SearchPlayerOfLeague(searchFor, season, gateWay, gameMode);

        var playerStats = await _playerRepository.SearchForPlayer(searchFor);

        var unrankedPlayers = playerStats.Select(s => s.CreateUnrankedResponse());

        playerRanks.AddRange(unrankedPlayers);

        return Ok(playerRanks);
    }

    [HttpGet("{leagueId}")]
    public async Task<IActionResult> GetLadder([FromRoute] int leagueId, int season, GateWay gateWay = GateWay.Europe, GameMode gameMode = GameMode.GM_1v1)
    {
        var playersInLadder = await _rankQueryHandler.LoadPlayersOfLeague(leagueId, season, gateWay, gameMode);

        if (playersInLadder == null)
        {
            return NoContent();
        }

        foreach (var entityInLadder in playersInLadder)
        {
            foreach (var playerInLadder in entityInLadder.PlayersInfo)
            {
                playerInLadder.PlayerAkaData = await _playerAkaProvider.GetPlayerAkaDataAsync(playerInLadder.BattleTag.ToLower());
            }
        }

        return Ok(playersInLadder);
    }

    [HttpGet("country/{countryCode}")]
    public async Task<IActionResult> GetCountryLadder([FromRoute] string countryCode, int season, GateWay gateWay = GateWay.Europe, GameMode gameMode = GameMode.GM_1v1)
    {
        var playersByCountry = await _rankQueryHandler.LoadPlayersOfCountry(countryCode, season, gateWay, gameMode);

        if (playersByCountry == null)
        {
            return NoContent();
        }

        return Ok(playersByCountry);
    }

    [HttpGet("league-constellation")]
    public async Task<IActionResult> GetLeagueConstellation(int season)
    {
        var leagues = await _rankRepository.LoadLeagueConstellation(season);
        return Ok(leagues);
    }

    [HttpGet("seasons")]
    public async Task<IActionResult> GetLeagueSeasons()
    {
        var seasons = await _rankRepository.LoadSeasons();
        return Ok(seasons.OrderByDescending(s => s.Id));
    }

    [HttpGet("active-modes")]
    public async Task<IActionResult> GetActiveGameModes()
    {
        try
        {
            var currentlyActiveModes = await _matchmakingProvider.GetCurrentlyActiveGameModesAsync();
            return Ok(currentlyActiveModes);
        }
        catch (HttpRequestException ex)
        {
            int statusCode = ex.StatusCode is null ? 500 : (int)ex.StatusCode;
            return StatusCode(statusCode, ex.Message);
        }
    }
}
