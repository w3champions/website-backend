using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.Ladder
{
    [ApiController]
    [Route("api/ladder")]
    public class LadderController : ControllerBase
    {
        private readonly IRankRepository _rankRepository;
        private readonly IPlayerRepository _playerRepository;
        private readonly RankQueryHandler _rankQueryHandler;
        private readonly PlayerAkaProvider _playerAkaProvider;

        public LadderController(
            IRankRepository rankRepository,
            IPlayerRepository playerRepository,
            RankQueryHandler rankQueryHandler,
            PlayerAkaProvider playerAkaProvider)
        {
            _rankRepository = rankRepository;
            _playerRepository = playerRepository;
            _rankQueryHandler = rankQueryHandler;
            _playerAkaProvider = playerAkaProvider;
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchPlayer(string searchFor, int season, GateWay gateWay = GateWay.Europe, GameMode
        gameMode = GameMode.GM_1v1)
        {
            System.Collections.Generic.List<Rank> playerRanks;

            playerRanks = await _rankRepository.SearchPlayerOfLeague(searchFor, season, gateWay, gameMode);

            var playerStats = await _playerRepository.SearchForPlayer(searchFor);

            var unrankedPlayers = playerStats.Select(s => s.CreateUnrankedResponse());

            playerRanks.AddRange(unrankedPlayers);

            return Ok(playerRanks);
        }

        [HttpGet("{leagueId}")]
        public async Task<IActionResult> GetLadder([FromRoute] int leagueId, int season, GateWay gateWay = GateWay.Europe, GameMode gameMode = GameMode.GM_1v1)
        {
            var playersInLadder = await _rankQueryHandler.LoadPlayersOfLeague(leagueId, season, gateWay, gameMode);

            if (playersInLadder == null)
            {
                return NoContent();
            }

            foreach (var entityInLadder in playersInLadder)
            {
                foreach (var playerInLadder in entityInLadder.PlayersInfo)
                {
                    playerInLadder.PlayerAkaData = _playerAkaProvider.GetPlayerAkaData(playerInLadder.BattleTag.ToLower());
                }
            }

            return Ok(playersInLadder);
        }

        [HttpGet("country/{countryCode}")]
        public async Task<IActionResult> GetCountryLadder([FromRoute] string countryCode, int season, GateWay gateWay = GateWay.Europe, GameMode gameMode = GameMode.GM_1v1)
        {
            var playersByCountry = await _rankQueryHandler.LoadPlayersOfCountry(countryCode, season, gateWay, gameMode);

            if (playersByCountry == null)
            {
                return NoContent();
            }

            return Ok(playersByCountry);
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