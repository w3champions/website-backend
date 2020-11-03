using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Ladder
{
    [ApiController]
    [Route("api/ladder")]
    public class LadderController : ControllerBase
    {
        private readonly IRankRepository _rankRepository;
        private readonly IPlayerRepository _playerRepository;
        private readonly RankQueryHandler _rankQueryHandler;

        public LadderController(
            IRankRepository rankRepository,
            IPlayerRepository playerRepository,
            RankQueryHandler rankQueryHandler)
        {
            _rankRepository = rankRepository;
            _playerRepository = playerRepository;
            _rankQueryHandler = rankQueryHandler;
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchPlayer(string searchFor, int season, GateWay gateWay = GateWay.Europe, GameMode
        gameMode = GameMode.GM_1v1)
        {
            var playerRanks = await _rankRepository.SearchPlayerOfLeague(searchFor, season, gateWay, gameMode);

            if (playerRanks.Any()) return Ok(playerRanks);

            var playerStats = await _playerRepository.SearchForPlayer(searchFor);

            return Ok(playerStats.Select(s => s.CreateUnrankedResponse()));
        }

        [HttpGet("{leagueId}")]
        public async Task<IActionResult> GetLadder([FromRoute] int leagueId, int season, GateWay gateWay = GateWay.Europe, GameMode gameMode = GameMode.GM_1v1)
        {
            var playersInLadder = await _rankQueryHandler.LoadPlayersOfLeague(leagueId, season, gateWay, gameMode);

            if (playersInLadder == null)
            {
                return NoContent();
            }

            return Ok(playersInLadder);
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
    }
}