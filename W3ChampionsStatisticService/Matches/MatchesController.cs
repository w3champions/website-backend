using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.Ports;
using System.Collections.Generic;
using W3ChampionsStatisticService.Cache;
using System;
using W3ChampionsStatisticService.PersonalSettings;
using System.Linq;

namespace W3ChampionsStatisticService.Matches
{
    [ApiController]
    [Route("api/matches")]
    public class MatchesController : ControllerBase
    {
        public static Dictionary<GameMode,CachedData<List<Matchup>>> _matchesCache;
        public static Dictionary<GameMode, CachedData<long>> _matchesCountCache;
        private readonly IMatchRepository _matchRepository;
        private readonly MatchQueryHandler _matchQueryHandler;

        public MatchesController(IMatchRepository matchRepository, MatchQueryHandler matchQueryHandler)
        {
            _matchRepository = matchRepository;
            _matchQueryHandler = matchQueryHandler;

            _matchesCache = new Dictionary<GameMode, CachedData<List<Matchup>>>();
            _matchesCountCache = new Dictionary<GameMode, CachedData<long>>();
            foreach (GameMode gm in Enum.GetValues(typeof(GameMode)))
            {
                _matchesCache.Add(gm, new CachedData<List<Matchup>>(() => FetchMatchDataSync(gm), TimeSpan.FromMinutes(1)));
                _matchesCountCache.Add(gm,new CachedData<long>(() => FetchMatchCountSync(gm), TimeSpan.FromMinutes(1)));
            }
            
        }
        public List<Matchup> FetchMatchDataSync(GameMode gm)
        {
            try
            {
                return FetchMatchData(gm).GetAwaiter().GetResult();
            }
            catch
            {
                return new List<Matchup>();
            }
        }


        private Task<List<Matchup>> FetchMatchData(GameMode gm)
        {
            GameMode gameMode = gm;
            GateWay gateWay = GateWay.Undefined;
            string map = "Overall";
            int minMmr = 0;
            int maxMmr = 3000;
            int offset = 0;
            int pageSize = 500;
            return _matchRepository.Load(gateWay, gm, offset, pageSize, map, minMmr, maxMmr);
        }
        public long FetchMatchCountSync(GameMode gm)
        {
            try
            {
                return FetchMatchCount(gm).GetAwaiter().GetResult();
            }
            catch
            {
                return 0;
            }
        }


        private Task<long> FetchMatchCount(GameMode gm)
        {
            GateWay gateWay = GateWay.Undefined;
            string map = "Overall";
            int minMmr = 0;
            int maxMmr = 3000;
            return _matchRepository.Count(gateWay, gm, map, minMmr, maxMmr);
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
            List<Matchup> matches = new List<Matchup>();
            long count = 0;
            if (pageSize > 100) pageSize = 100;
            if (offset < 500 && (offset + pageSize) < 501 && map=="Overall" && minMmr==0 && maxMmr==3000)
            {
                _matchesCache[gameMode].GetCachedData().Skip(offset).Take(pageSize).ToList();
            }
            else
            {
                matches = await _matchRepository.Load(gateWay, gameMode, offset, pageSize, map, minMmr, maxMmr);
            }
            count = _matchesCountCache[gameMode].GetCachedData();
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
}
