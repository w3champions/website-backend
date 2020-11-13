using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    [ApiController]
    [Route("api/players")]
    public class PlayersController : ControllerBase
    {
        private readonly IPlayerRepository _playerRepository;
        private readonly GameModeStatQueryHandler _queryHandler;

        public PlayersController(
            IPlayerRepository playerRepository,
            GameModeStatQueryHandler queryHandler)
        {
            _playerRepository = playerRepository;
            _queryHandler = queryHandler;
        }

        [HttpGet("{battleTag}")]
        public async Task<IActionResult> GetPlayer([FromRoute] string battleTag)
        {
            var player = await _playerRepository.LoadPlayerProfile(battleTag);
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

        [HttpGet("{battleTag}/mmr-timeline")]
        public async Task<IActionResult> GetPlayerMmrTimeline(
            [FromRoute] string battleTag,
            Race race,
            GateWay gateWay,
            int season,
            GameMode gameMode)
        {
            var playerMmrTimeline = await _playerRepository.LoadPlayerMmrTimeline(battleTag, race, gateWay, season, gameMode);
            return Ok(playerMmrTimeline);
        }
    }
}