using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Ladder
{
    [ApiController]
    [Route("api/ladder")]
    public class LadderController : ControllerBase
    {
        private readonly IPlayerRepository _playerRepository;
        private readonly IMatchEventRepository _ladderRepository;

        public LadderController(
            IPlayerRepository playerRepository,
            IMatchEventRepository ladderRepository)
        {
            _playerRepository = playerRepository;
            _ladderRepository = ladderRepository;
        }

        [HttpGet]
        public async Task<IActionResult> GetLadder(int offset, int pageSize, int gateWay)
        {
            var matches = await _playerRepository.LoadOverviewSince(offset, pageSize, gateWay);
            return Ok(matches);
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchPlayer(string searchFor, int gateWay = 20)
        {
            var players = await _playerRepository.LoadOverviewLike(searchFor, gateWay);
            return Ok(players);
        }

        [HttpGet("{ladderId}")]
        public async Task<IActionResult> GetLadder([FromRoute] int ladderId, int gateWay = 20)
        {
            var ladder = await _ladderRepository.LoadRank(ladderId, gateWay);
            if (ladder == null)
            {
                return NoContent();
            }

            return Ok(new Division(ladder));
        }
    }

    public class Division
    {
        public int Gateway { get; set; }
        public List<PlayerRank> Players { get; set; }

        public Division(RankingChangedEvent ladder)
        {
            Gateway = ladder.gateway;
            Players = ladder.ranks.Select((r, index )=> new PlayerRank(index + 1, r.id, r.rp)).ToList();
        }
    }

    public class PlayerRank
    {
        public int Rank { get; }
        public string BattleTag { get; }
        public double RankPoints { get; }

        public PlayerRank(int rank, string battleTag, in double rankPoints)
        {
            Rank = rank;
            BattleTag = battleTag;
            RankPoints = rankPoints;
        }
    }
}