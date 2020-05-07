using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;
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

            PopulateStats(leaguesOfPlayer, player, allLeagues, GameMode.GM_1v1, GateWay.Europe);
            PopulateStats(leaguesOfPlayer, player, allLeagues, GameMode.GM_1v1, GateWay.Usa);
            PopulateStats(leaguesOfPlayer, player, allLeagues, GameMode.GM_2v2_AT, GateWay.Europe);
            PopulateStats(leaguesOfPlayer, player, allLeagues, GameMode.GM_2v2_AT, GateWay.Usa);

            return Ok(player);
        }

        //way to shitty, do this with better rm one day
        private static void PopulateStats(
            List<Rank> leaguesOfPlayer,
            PlayerProfile player,
            List<LeagueConstellationChangedEvent> allLeagues,
            GameMode gameMode,
            GateWay gateWay)
        {
            var gameModeIndex = gameMode switch
            {
                GameMode.GM_1v1 => 0,
                GameMode.GM_2v2_AT => 1,
                _ => 0
            };
            
            var searchedLeagues = leaguesOfPlayer.FirstOrDefault(l => l.GameMode == gameMode && l.Gateway == gateWay);
            if (searchedLeagues != null)
            {
                player.GateWayStats
                    .Single(g => g.GateWay == gateWay)
                    .GameModeStats[gameModeIndex].Rank = searchedLeagues.RankNumber;
                player.GateWayStats
                    .Single(g => g.GateWay == gateWay)
                    .GameModeStats[gameModeIndex].LeagueId = searchedLeagues.League;
                player.GateWayStats
                    .Single(g => g.GateWay == gateWay)
                    .GameModeStats[gameModeIndex].LeagueOrder = allLeagues
                        .Single(l => l.gateway == gateWay && l.gameMode == gameMode)
                        .leagues
                        .Single(l => l.id == searchedLeagues.League).order;;
            }
        }

        [HttpGet("{battleTag}/winrate")]
        public async Task<IActionResult> GetPlayerWinrate([FromRoute] string battleTag)
        {
            var wins = await _playerRepository.LoadPlayerWinrate(battleTag);
            return Ok(wins);
        }
    }
}