using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Ports;
using W3C.Contracts.GameObjects;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.Matches;

[ApiController]
[Route("api/matches")]
public class MatchesController(IMatchRepository matchRepository, MatchQueryHandler matchQueryHandler, MatchService matchService) : ControllerBase
{
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly MatchQueryHandler _matchQueryHandler = matchQueryHandler;
    private readonly MatchService _matchService = matchService;

    [HttpGet("")]
    public async Task<IActionResult> GetMatches(
        int offset = 0,
        int pageSize = 100,
        GameMode gameMode = GameMode.Undefined,
        int season = -1)
    {
        if (season < 0)
        {
            var lastSeason = await _matchRepository.LoadLastSeason();
            season = lastSeason.Id;
        }
        if (pageSize > 100) pageSize = 100;
        var matches = await _matchRepository.Load(season, gameMode, offset, pageSize);
        var count = await _matchRepository.Count(season, gameMode);
        return Ok(new { matches, count });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetMatcheDetails(string id)
    {
        var match = await _matchRepository.LoadDetails(new ObjectId(id));
        return Ok(match);
    }

    [HttpGet("gameName/{gameName}")]
    public async Task<IActionResult> GetMatchIdFromGameName(string gameName)
    {
        var match = await _matchRepository.LoadDetailsByGameName(gameName);
        if (match == null) return NotFound();
        return Ok(match.Id.ToString());
    }

    [HttpGet("by-ongoing-match-id/{id}")]
    public async Task<IActionResult> GetMatcheDetailsByOngoingMatchId(string id)
    {
        var match = await _matchRepository.LoadDetailsByOngoingMatchId(id);
        return Ok(match);
    }

    [HttpGet("search")]
    public async Task<IActionResult> GetMatchesPerPlayer(
        string playerId,
        int season,
        string opponentId = null,
        GameMode gameMode = GameMode.Undefined,
        GateWay gateWay = GateWay.Undefined,
        Race playerRace = Race.Total,
        Race opponentRace = Race.Total,
        int offset = 0,
        int pageSize = 100)
    {
        if (pageSize > 100) pageSize = 100;
        
        var matches = matchService.GetMatchesPerPlayer(playerId, season, opponentId, gameMode, gateWay, playerRace, opponentRace, pageSize, offset);
        var count = matchService.GetMatchCountPerPlayer(playerId, season, opponentId, gameMode, gateWay, playerRace, opponentRace);
        return Ok(new { matches, count });
    }


    [HttpGet("ongoing")]
    public async Task<IActionResult> GetOnGoingMatches(
        int offset = 0,
        int pageSize = 100,
        GameMode gameMode = GameMode.Undefined,
        GateWay gateWay = GateWay.Undefined,
        string map = "Overall",
        int minMmr = 0,
        int maxMmr = 3000,
        string sort = "startTimeDescending"
        )
    {
        if (pageSize > 200) pageSize = 200;
        var matches = await _matchRepository.LoadOnGoingMatches(gameMode, gateWay, offset, pageSize, map, minMmr, maxMmr, sort);
        var count = await _matchRepository.CountOnGoingMatches(gameMode, gateWay, map, minMmr, maxMmr);

        await _matchQueryHandler.PopulatePlayerInfos(matches);

        PlayersObfuscator.ObfuscatePlayersForFFA(matches.ToArray());

        return Ok(new { matches, count });
    }

    [HttpGet("ongoing/{playerId}")]
    public async Task<IActionResult> GetOnGoingMatches(string playerId)
    {
        var onGoingMatch = await _matchRepository.TryLoadOnGoingMatchForPlayer(playerId);

        if (onGoingMatch != null && onGoingMatch.GameMode == GameMode.FFA)
        {
            return Ok(null);
        }

        PlayersObfuscator.ObfuscatePlayersForFFA(onGoingMatch);

        return Ok(onGoingMatch);
    }
}
