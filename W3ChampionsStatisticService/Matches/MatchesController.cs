using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Heroes;
using W3ChampionsStatisticService.Ports;
using W3C.Contracts.GameObjects;
using W3ChampionsStatisticService.Services;
using W3C.Domain.Tracing;
using W3C.Domain.GameModes;
using W3ChampionsStatisticService.Common.Constants;
using W3ChampionsStatisticService.W3ChampionsStats.MmrDistribution;

namespace W3ChampionsStatisticService.Matches;

[ApiController]
[Route("api/matches")]
[Trace]
public class MatchesController(
    IMatchRepository matchRepository,
    MatchQueryHandler matchQueryHandler,
    MatchService matchService,
    IPlayerRepository playerRepository,
    MmrDistributionHandler mmrDistributionHandler
) : ControllerBase
{
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly IPlayerRepository _playerRepository = playerRepository;
    private readonly MatchQueryHandler _matchQueryHandler = matchQueryHandler;
    private readonly MatchService _matchService = matchService;
    private readonly MmrDistributionHandler _mmrDistributionHandler = mmrDistributionHandler;


    [HttpGet("")]
    public async Task<IActionResult> GetMatches(
        int offset = 0,
        int pageSize = 100,
        GameMode gameMode = GameMode.Undefined,
        GateWay gateWay = GateWay.Undefined,
        int season = -1,
        HeroType hero = HeroType.AllFilter,
        int minMmr = 0,
        int maxMmr = -1,
        int minPercentile = 0,
        int maxPercentile = 0
    )
    {
        if (maxMmr < 0) maxMmr = MmrConstants.MaxMmrPerGameMode[gameMode];
        if (minPercentile > 0 || maxPercentile > 0)
        {
            if (minPercentile < 0 || minPercentile > 100) return BadRequest("minPercentile must be between 0 and 100");
            if (maxPercentile < 0 || maxPercentile > 100) return BadRequest("maxPercentile must be between 0 and 100");
            if (minPercentile >= maxPercentile) return BadRequest("minPercentile must be less than maxPercentile");
            (minMmr, maxMmr) = await _mmrDistributionHandler.GetPercentileMmr(season, gateWay, gameMode, minPercentile, maxPercentile);
        }
        if (season < 0)
        {
            var lastSeason = await _matchRepository.LoadLastSeason();
            season = lastSeason.Id;
        }
        if (pageSize > 100) pageSize = 100;
        var matches = await _matchRepository.Load(season, gameMode, offset, pageSize, hero, minMmr, maxMmr);
        var count = await _matchRepository.Count(season, gameMode, hero, minMmr, maxMmr);
        return Ok(new { matches, count });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetMatchDetails(string id)
    {
        var match = await _matchRepository.LoadFinishedMatchDetails(new ObjectId(id));
        return Ok(match);
    }

    [HttpGet("gameName/{gameName}")]
    public async Task<IActionResult> GetMatchIdFromGameName(string gameName)
    {
        var match = await _matchRepository.LoadMatchFinishedEventByGameName(gameName);
        if (match == null) return NotFound();
        return Ok(match.Id.ToString());
    }

    [HttpGet("by-ongoing-match-id/{id}")]
    public async Task<IActionResult> GetMatchDetailsByOngoingMatchId(string id)
    {
        var match = await _matchRepository.LoadFinishedMatchDetailsByMatchId(id);
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
        int pageSize = 100,
        HeroType hero = HeroType.AllFilter)
    {
        if (pageSize > 100) pageSize = 100;

        var matches = await _matchService.GetMatchesPerPlayer(playerId, season, opponentId, gameMode, gateWay, playerRace, opponentRace, offset, pageSize, hero);
        var count = await _matchService.GetMatchCountPerPlayer(playerId, season, opponentId, gameMode, gateWay, playerRace, opponentRace, hero);
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
        int maxMmr = -1,
        string sort = "startTimeDescending"
        )
    {
        if (maxMmr < 0) maxMmr = MmrConstants.MaxMmrPerGameMode[gameMode];
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

        if (onGoingMatch != null && GameModesHelper.IsFfaGameMode(onGoingMatch.GameMode))
        {
            return Ok(null);
        }

        PlayersObfuscator.ObfuscatePlayersForFFA(onGoingMatch);

        return Ok(onGoingMatch);
    }
}
