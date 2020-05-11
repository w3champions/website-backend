using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    [ApiController]
    [Route("api/players")]
    public class PlayersController : ControllerBase
    {
        private readonly IPlayerRepository _playerRepository;
        private readonly IRankRepository _rankRepository;
        private readonly PlayerQueryHandler _playerQueryHandler;

        public PlayersController(
            IPlayerRepository playerRepository,
            IRankRepository rankRepository,
            PlayerQueryHandler playerQueryHandler)
        {
            _playerRepository = playerRepository;
            _rankRepository = rankRepository;
            _playerQueryHandler = playerQueryHandler;
        }

        [HttpGet("{battleTag}")]
        public async Task<IActionResult> GetPlayer([FromRoute] string battleTag)
        {
            var player = await _playerQueryHandler.Get(battleTag);
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