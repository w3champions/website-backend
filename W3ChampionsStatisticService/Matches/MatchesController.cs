using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Matches
{
    [ApiController]
    [Route("api/matches")]
    public class MatchesController : ControllerBase
    {
        private readonly IMatchRepository _matchRepository;
        private readonly MatchQueryHandler _matchQueryHandler;
        private readonly IPersonalSettingsRepository _personalSettingsRepository;

        public MatchesController(
            IMatchRepository matchRepository,
            MatchQueryHandler matchQueryHandler,
            IPersonalSettingsRepository personalSettingsRepository)
        {
            _matchRepository = matchRepository;
            _matchQueryHandler = matchQueryHandler;
            _personalSettingsRepository = personalSettingsRepository;
        }

        [HttpGet("")]
        public async Task<IActionResult> GetMatches(
            int offset = 0,
            int pageSize = 100,
            GameMode gameMode = GameMode.Undefined,
            GateWay gateWay = GateWay.Undefined,
            string map = "Overall")
        {
            if (pageSize > 100) pageSize = 100;
            var matches = await _matchRepository.Load(gateWay, gameMode, offset, pageSize, map);
            var count = await _matchRepository.Count(gateWay, gameMode, map);

            await AssignLocationsToMatchups(matches);


            return Ok(new { matches, count });
        }

        private async Task<List<Matchup>> AssignLocationsToMatchups(List<Matchup> matches)
        {
            var battleTags = new List<string>();
            var players = new Dictionary<PlayerOverviewMatches, Matchup>();
            var matchesToUpdate = new HashSet<Matchup>();

            // Get all the players for the matches that are missing
            // location or country code.

            foreach (var match in matches)
            {
                foreach (var team in match.Teams)
                {
                    foreach (var player in team.Players)
                    {
                        if (player.Location == null && player.CountryCode == null)
                        {
                            players.Add(player, match);
                            battleTags.Add(player.BattleTag);
                        }

                    }
                }
            }

            // Bulk load their personal settings as <BattleTag, PersonalSettings> dict

            var personalSettings =
                (await _personalSettingsRepository.LoadMany(battleTags.ToArray()))
                    .ToDictionary(ps => ps.Id, ps => ps);


            // Try to fill in location with their personal settings data
            foreach (var player in players)
            {
                if (personalSettings.TryGetValue(player.Key.BattleTag, out PersonalSetting ps))
                {
                    player.Key.CountryCode ??= ps.CountryCode;
                    player.Key.Location ??= ps.Location;
                    player.Key.Country ??= ps.Country;

                    // Queue the matchup to be updated
                    matchesToUpdate.Add(player.Value);
                }
            }


            // Update all the matches
            foreach (var match in matchesToUpdate)
            {
                await _matchRepository.Insert(match);
            }

            return matches;
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
            int offset = 0,
            int pageSize = 100)
        {
            if (pageSize > 100) pageSize = 100;
            var matches = await _matchRepository.LoadFor(playerId, opponentId, gateWay, gameMode, pageSize, offset, season);
            var count = await _matchRepository.CountFor(playerId, opponentId, gateWay, gameMode, season);
            return Ok(new { matches, count });
        }


        [HttpGet("ongoing")]
        public async Task<IActionResult> GetOnGoingMatches(
            int offset = 0,
            int pageSize = 100,
            GameMode gameMode = GameMode.Undefined,
            GateWay gateWay = GateWay.Undefined,
            string map = "Overall")
        {
            if (pageSize > 200) pageSize = 200;
            var matches = await _matchRepository.LoadOnGoingMatches(gameMode, gateWay, offset, pageSize, map);
            var count = await _matchRepository.CountOnGoingMatches(gameMode, gateWay, map);

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