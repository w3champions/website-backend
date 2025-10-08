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
using System;

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


    /// <summary>
    /// Gets a paginated list of matches with optional filters.
    /// </summary>
    /// <param name="offset">The offset for pagination.</param>
    /// <param name="pageSize">The number of matches per page (max 100).</param>
    /// <param name="gameMode">The game mode filter.</param>
    /// <param name="gateWay">The gateway filter.</param>
    /// <param name="season">The season filter. If less than 0, uses the latest season.</param>
    /// <param name="hero">The hero filter.</param>
    /// <param name="minMmr">The minimum MMR filter.</param>
    /// <param name="maxMmr">The maximum MMR filter.</param>
    /// <param name="minPercentile">The minimum percentile filter.</param>
    /// <param name="maxPercentile">The maximum percentile filter.</param>
    /// <returns>
    /// 200 OK: An object containing a list of matches and the total count.
    /// {
    ///   matches: List&lt;MatchFinishedEvent&gt;,
    ///   count: int
    /// }
    /// </returns>
    [ProducesResponseType(typeof(object), 200)]
    [HttpGet("")]
    public async Task<IActionResult> GetMatches(
        int offset = 0,
        int pageSize = 100,
        GameMode gameMode = GameMode.Undefined,
        GateWay gateWay = GateWay.Undefined,
        int season = -1,
        HeroType hero = HeroType.AllFilter,
        int minMmr = 0,
        int? maxMmr = null,
        int? minPercentile = null,
        int? maxPercentile = null
    )
    {
        if (maxMmr == null) maxMmr = MmrConstants.MaxMmrPerGameMode[gameMode];
        if (minPercentile != null || maxPercentile != null)
        {
            var minPercentileParam = minPercentile ?? 0;
            var maxPercentileParam = maxPercentile ?? 100;
            if (maxPercentileParam < 0 || maxPercentileParam > 100) return BadRequest("maxPercentile must be between 0 and 100");
            if (minPercentileParam >= maxPercentileParam) return BadRequest("minPercentile must be less than maxPercentile");
            var (mmr1, mmr2) = await _mmrDistributionHandler.GetPercentileMmr(season, gateWay, gameMode, minPercentileParam, maxPercentileParam);
            minMmr = Math.Min(mmr1, mmr2);
            maxMmr = Math.Max(mmr1, mmr2);
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
        if (!ObjectId.TryParse(id, out var objectId))
        {
            return BadRequest($"Invalid match ID format: {id}");
        }

        var match = await _matchRepository.LoadFinishedMatchDetails(objectId);
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
        int? maxMmr = null,
        string sort = "startTimeDescending"
        )
    {
        if (maxMmr == null) maxMmr = MmrConstants.MaxMmrPerGameMode[gameMode];
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
