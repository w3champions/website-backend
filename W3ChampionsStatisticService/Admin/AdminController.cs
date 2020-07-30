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

        private readonly PadServiceRepo _padServiceRepository;
        private readonly INewsRepository _newsRepository;

        public AdminController(
            IMatchRepository matchRepository,
            PadServiceRepo padServiceRepository,
            INewsRepository newsRepository)
        {
            _matchRepository = matchRepository;
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

        [HttpGet("bannedPlayers")]
        public async Task<IActionResult> GetBannedPlayers()
        {
            var bannedPlayers = await _padServiceRepository.GetBannedPlayers();
            return Ok(bannedPlayers);
        }

        [HttpPost("bannedPlayers")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> PostBannedPlayer([FromBody] BannedPlayer bannedPlayer)
        {
            var bannedPlayers = await _padServiceRepository.PostBannedPlayers(bannedPlayer);
            return Ok(bannedPlayers);
        }

        [HttpDelete("bannedPlayers")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> DeleteBannedPlayer([FromBody] BannedPlayer bannedPlayer)
        {
            var bannedPlayers = await _padServiceRepository.DeleteBannedPlayers(bannedPlayer);
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