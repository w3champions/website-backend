using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using W3ChampionsStatisticService.PadEvents.PadSync;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Admin
{
    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly IMatchRepository _matchRepository;
        private readonly BanReadmodelRepository _banRepository;
        private readonly PadServiceRepo _padServiceRepository;
        private readonly INewsRepository _newsRepository;

        public AdminController(
            IMatchRepository matchRepository,
            PadServiceRepo padServiceRepository,
            BanReadmodelRepository banRepository,
            INewsRepository newsRepository)
        {
            _matchRepository = matchRepository;
            _banRepository = banRepository;
            _padServiceRepository = padServiceRepository;
            _newsRepository = newsRepository;
        }

        [HttpGet("health-check")]
        public IActionResult HealthCheck()
        {
            return Ok();
        }

        [HttpGet("db-health-check")]
        public async Task<IActionResult> DatabaseHealthCheck()
        {
            var countOnGoingMatches = await _matchRepository.CountOnGoingMatches();
            return Ok(countOnGoingMatches);
        }

        [HttpGet("broken-route")]
        public async Task<IActionResult> BrokenRouteTest()
        {
            BannedPlayerReadmodel bp = null;
            return Ok(bp.banReason);
        }


        [HttpGet("bannedPlayers")]
        public async Task<IActionResult> GetBannedPlayers()
        {
            var bannedPlayers = await _banRepository.GetBans();
            return Ok(new BannedPlayerResponse { total = bannedPlayers.Count, players = bannedPlayers });
        }

        [HttpPost("bannedPlayers")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> PostBannedPlayer([FromBody] BannedPlayerReadmodel bannedPlayerReadmodel)
        {
            var bannedPlayers = await _padServiceRepository.PostBannedPlayers(bannedPlayerReadmodel);
            return Ok(bannedPlayers);
        }

        [HttpDelete("bannedPlayers")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> DeleteBannedPlayer([FromBody] BannedPlayerReadmodel bannedPlayerReadmodel)
        {
            var bannedPlayers = await _padServiceRepository.DeleteBannedPlayers(bannedPlayerReadmodel);
            return Ok(bannedPlayers);
        }

        [HttpGet("news")]
        public async Task<IActionResult> GetNews(int? limit)
        {
            return Ok(await _newsRepository.Get(limit));
        }

        [HttpPut("news/{newsId}")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> UpdateNews(string newsId, [FromBody] NewsMessage newsMessage)
        {
            newsMessage.Id = new ObjectId(newsId);
            await _newsRepository.UpsertNews(newsMessage);
            return Ok();
        }

        [HttpPut("news")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> UpdateNews([FromBody] NewsMessage newsMessage)
        {
            newsMessage.Id = ObjectId.GenerateNewId();
            await _newsRepository.UpsertNews(newsMessage);
            return Ok();
        }

        [HttpDelete("news/{newsId}")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> DeleteNews(string newsId)
        {
            await _newsRepository.DeleteNews(new ObjectId(newsId));
            return Ok();
        }
    }
}