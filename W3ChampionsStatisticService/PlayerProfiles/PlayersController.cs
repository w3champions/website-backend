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

        [HttpGet("{battleTag}")]
        public async Task<IActionResult> GetPlayer([FromRoute] string battleTag)
        {
            var player = await _playerRepository.Load(battleTag) ?? PlayerProfile.Default();
            var leaguesOfPlayer = await _rankRepository.LoadPlayerOfLeague(battleTag);
            var allLeagues = await _rankRepository.LoadLeagueConstellation();
            var gw = int.Parse(battleTag.Split("@")[1]);

            var loadPlayerOfLeagueLike1V1 = leaguesOfPlayer.FirstOrDefault(l => l.GameMode == GameMode.GM_1v1);
            if (loadPlayerOfLeagueLike1V1 != null)
            {
                player.GameModeStats[0].Rank = loadPlayerOfLeagueLike1V1.RankNumber;
                player.GameModeStats[0].LeagueId = loadPlayerOfLeagueLike1V1.League;
                player.GameModeStats[0].LeagueOrder = allLeagues.Single(l => l.gateway == gw && l.gameMode == GameMode.GM_1v1).leagues
                    .Single(l => l.id == loadPlayerOfLeagueLike1V1.League).order;
            }

            var loadPlayerOfLeagueLike2V2 = leaguesOfPlayer.FirstOrDefault(l => l.GameMode == GameMode.GM_2v2_AT);
            if (loadPlayerOfLeagueLike2V2 != null)
            {
                player.GameModeStats[1].Rank = loadPlayerOfLeagueLike2V2.RankNumber;
                player.GameModeStats[1].LeagueId = loadPlayerOfLeagueLike2V2.League;
                player.GameModeStats[1].LeagueOrder = allLeagues.Single(l => l.gateway == gw && l.gameMode == GameMode.GM_2v2_AT).leagues
                    .Single(l => l.id == loadPlayerOfLeagueLike2V2.League).order;
            }

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