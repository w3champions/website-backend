using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    [ApiController]
    [Route("api/players")]
    public class PlayersController : ControllerBase
    {
        private readonly IPlayerRepository _playerRepository;
        private readonly IRankRepository _rankRepository;

        public PlayersController(IPlayerRepository playerRepository, IRankRepository rankRepository)
        {
            _playerRepository = playerRepository;
            _rankRepository = rankRepository;
        }

        [HttpGet]
        public async Task<IActionResult> GetPlayer([FromRoute] string battleTag)
        {
            var player = await _playerRepository.Load(battleTag) ?? PlayerProfile.Default();
            var gm1v1 = await _rankRepository.LoadPlayerOfLeague(battleTag, GameMode.GM_1v1);
            var gm2v2 = await _rankRepository.LoadPlayerOfLeague(battleTag, GameMode.GM_2v2);
            var leagues = await _rankRepository.LoadLeagueConstellation();
            var gw = int.Parse(battleTag.Split("@")[1]);
            var constellationOfGw = leagues.Single(l => l.gateway == gw);
            var league1v1 = constellationOfGw.leagues.Single(l => l.id == gm1v1.League);
            var league2v2 = constellationOfGw.leagues.Single(l => l.id == gm2v2.League);

            player.GameModeStats[0].Rank = gm1v1.RankNumber;
            player.GameModeStats[0].LeagueId = gm1v1.League;
            player.GameModeStats[0].LeagueOrder = league1v1.order;

            player.GameModeStats[0].Rank = gm2v2.RankNumber;
            player.GameModeStats[0].LeagueId = gm2v2.League;
            player.GameModeStats[0].LeagueOrder = league2v2.order;

            return Ok(player);
        }

        [HttpGet("{battleTag}/winrate")]
        public async Task<IActionResult> GetPlayerWinrate([FromRoute] string battleTag)
        {
            var wins = await _playerRepository.LoadPlayerWinrate(battleTag);
            return Ok(wins);
        }
    }
}