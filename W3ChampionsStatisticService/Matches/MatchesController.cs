using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.GameModes;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Common.Constants;
using W3ChampionsStatisticService.Heroes;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;
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

    /// <summary>
    /// Gets the distinct map names for a season and game mode.
    /// </summary>
    /// <param name="season">The season filter.</param>
    /// <param name="gameMode">The game mode filter.</param>
    /// <returns>
    /// 200 OK: The array of distinct map names from the season
    /// </returns>
    [ProducesResponseType(typeof(List<string>), 200)]
    [HttpGet("map-names")]
    public async Task<IActionResult> GetMapNames(
        GameMode gameMode,
        int season
    )
    {
        var mapNames = await _matchService.GetMapNames(season, gameMode);
        return Ok(mapNames);
    }

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
    /// <param name="minDuration">The minimum match duration in seconds (recommended minimum: 300s).<filter.</param>
    /// <param name="maxDuration">The maximum match duration in seconds (recommended maximum: 99999s)<.filter.</param>
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
        int? maxPercentile = null,
        int? minDuration = null,
        int? maxDuration = null,
        string mapName = "Overall"
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
        var matches = await _matchRepository.Load(season, gameMode, offset, pageSize, hero, minMmr, maxMmr, minDuration, maxDuration, mapName);
        PlayersObfuscator.ObfuscateMmr(matches);
        var count = await _matchRepository.Count(season, gameMode, hero, minMmr, maxMmr, minDuration, maxDuration, mapName);
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
        PlayersObfuscator.ObfuscateMmr(match);
        return Ok(match);
    }

    [HttpGet("gameName/{gameName}")]
    public async Task<IActionResult> GetMatchIdFromGameName(string gameName)
    {
        var match = await _matchRepository.LoadMatchFinishedEventByGameName(gameName);
        if (match == null) return NotFound();
        PlayersObfuscator.ObfuscateMmr(match);
        return Ok(match.Id.ToString());
    }

    [HttpGet("by-ongoing-match-id/{id}")]
    public async Task<IActionResult> GetMatchDetailsByOngoingMatchId(string id)
    {
        var match = await _matchRepository.LoadFinishedMatchDetailsByMatchId(id);
        PlayersObfuscator.ObfuscateMmr(match);
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
        HeroType hero = HeroType.AllFilter,
        bool playerIncludeRandom = false,
        bool opponentIncludeRandom = false)
    {
        if (pageSize > 100) pageSize = 100;

        var matches = await _matchService.GetMatchesPerPlayer(playerId, season, opponentId, gameMode, gateWay, playerRace, opponentRace, offset, pageSize, hero, playerIncludeRandom, opponentIncludeRandom);
        var count = await _matchService.GetMatchCountPerPlayer(playerId, season, opponentId, gameMode, gateWay, playerRace, opponentRace, hero, playerIncludeRandom, opponentIncludeRandom);
        PlayersObfuscator.ObfuscateMmr(matches);
        return Ok(new { matches, count });
    }


    /// <summary>
    /// Searches the players a given player shares finished matches with,
    /// e.g. to suggest opponents when filtering a player's match history.
    /// </summary>
    /// <param name="playerId">The battleTag whose matches are searched.</param>
    /// <param name="season">The season filter. If less than 0, uses the latest season.</param>
    /// <param name="search">Case-insensitive battleTag fragment. Empty returns the most played opponents.</param>
    /// <param name="gateWay">The gateway filter.</param>
    /// <param name="limit">The maximum number of results (max 50).</param>
    /// <returns>
    /// 200 OK: A list of players ordered by shared match count descending.
    /// [{ battleTag: string, matchCount: long }]
    /// </returns>
    [ProducesResponseType(typeof(List<OpponentInfo>), 200)]
    [HttpGet("search-opponents")]
    public async Task<IActionResult> SearchOpponents(
        string playerId,
        int season = -1,
        string search = "",
        GateWay gateWay = GateWay.Undefined,
        int limit = 10)
    {
        if (string.IsNullOrEmpty(playerId)) return BadRequest("playerId is required");
        if (season < 0)
        {
            var lastSeason = await _matchRepository.LoadLastSeason();
            season = lastSeason.Id;
        }
        if (limit > 50) limit = 50;

        var opponents = await _matchService.SearchOpponentsPerPlayer(playerId, search, season, gateWay, limit);
        return Ok(opponents);
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
        PlayersObfuscator.ObfuscateMmr(matches);

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
        PlayersObfuscator.ObfuscateMmr(onGoingMatch);

        return Ok(onGoingMatch);
    }
}
