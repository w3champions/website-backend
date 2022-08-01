﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Matches
{
    [ApiController]
    [Route("api/matches")]
    public class MatchesController : ControllerBase
    {
        private readonly IMatchRepository _matchRepository;
        private readonly MatchQueryHandler _matchQueryHandler;

        public MatchesController(IMatchRepository matchRepository, MatchQueryHandler matchQueryHandler)
        {
            _matchRepository = matchRepository;
            _matchQueryHandler = matchQueryHandler;
        }

        [HttpGet("")]
        public async Task<IActionResult> GetMatches(
            int offset = 0,
            int pageSize = 100,
            GameMode gameMode = GameMode.Undefined,
            GateWay gateWay = GateWay.Undefined,
            string map = "Overall",
            int minMmr = 0,
            int maxMmr = 3000)
        {
            if (pageSize > 100) pageSize = 100;
            var matches = await _matchRepository.Load(gateWay, gameMode, offset, pageSize, map, minMmr, maxMmr);
            var count = await _matchRepository.Count(gateWay, gameMode, map, minMmr, maxMmr);
            return Ok(new { matches, count });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetMatcheDetails(string id)
        {
            var match = await _matchRepository.LoadDetails(new ObjectId(id));
            return Ok(match);
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
            var matches = await _matchRepository.LoadFor(playerId, opponentId, gateWay, gameMode, playerRace, opponentRace, pageSize, offset, season);
            var count = await _matchRepository.CountFor(playerId, opponentId, gateWay, gameMode, playerRace, opponentRace, season);
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
            int maxMmr = 3000)
        {
            if (pageSize > 200) pageSize = 200;
            var matches = await _matchRepository.LoadOnGoingMatches(gameMode, gateWay, offset, pageSize, map, minMmr, maxMmr);
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
}
