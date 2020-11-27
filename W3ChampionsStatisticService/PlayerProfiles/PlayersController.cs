using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    [ApiController]
    [Route("api/players")]
    public class PlayersController : ControllerBase
    {
        private readonly IPlayerRepository _playerRepository;
        private readonly GameModeStatQueryHandler _queryHandler;
        private readonly IW3CAuthenticationService _authenticationService;

        public PlayersController(
            IPlayerRepository playerRepository,
            GameModeStatQueryHandler queryHandler,
            IW3CAuthenticationService authenticationService)
        {
            _playerRepository = playerRepository;
            _queryHandler = queryHandler;
            _authenticationService = authenticationService;
        }

        [HttpGet("{battleTag}")]
        public async Task<IActionResult> GetPlayer([FromRoute] string battleTag, [FromQuery] string authorization)
        {
            var player = await _playerRepository.LoadPlayerProfile(battleTag);
            if (player == null && authorization != null)
            {
                var user = await _authenticationService.GetUserByToken(authorization);
                if (user == null)
                {
                    return Unauthorized("Sorry Hackerboi");
                }

                player = PlayerOverallStats.Create(battleTag);
                await _playerRepository.UpsertPlayer(player);
            }

            return Ok(player);
        }

        [HttpGet]
        public async Task<IActionResult> SearchPlayer(string search)
        {
            var players = await _playerRepository.SearchForPlayer(search);
            return Ok(players);
        }

        [HttpGet("{battleTag}/winrate")]
        public async Task<IActionResult> GetPlayerWinrate([FromRoute] string battleTag, int season)
        {
            var wins = await _playerRepository.LoadPlayerWinrate(battleTag, season);
            return Ok(wins);
        }

        [HttpGet("{battleTag}/game-mode-stats")]
        public async Task<IActionResult> GetGameModeStats(
            [FromRoute] string battleTag,
            GateWay gateWay,
            int season)
        {
            var wins = await _queryHandler.LoadPlayerStatsWithRanks(battleTag, gateWay, season);
            return Ok(wins);
        }

        [HttpGet("{battleTag}/race-stats")]
        public async Task<IActionResult> GetRaceStats(
            [FromRoute] string battleTag,
            GateWay gateWay,
            int season)
        {
            var wins = await _playerRepository.LoadRaceStatPerGateway(battleTag, gateWay, season);
            var ordered = wins.OrderBy(s => s.Race).ToList();
            var firstPick = ordered.FirstOrDefault();
            if (firstPick?.Race != Race.RnD) return Ok(ordered);

            ordered.Remove(firstPick);
            ordered.Add(firstPick);
            return Ok(ordered);
        }

        [HttpGet("{battleTag}/mmr-rp-timeline")]
        public async Task<IActionResult> GetPlayerMmrRpTimeline(
            [FromRoute] string battleTag,
            Race race,
            GateWay gateWay,
            int season,
            GameMode gameMode)
        {
            var playerMmrRpTimeline = await _playerRepository.LoadPlayerMmrRpTimeline(battleTag, race, gateWay, season, gameMode);
            return Ok(playerMmrRpTimeline);
        }
    }
}