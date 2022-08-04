using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3C.Domain.Repositories;
using W3C.Domain.CommonValueObjects;
using System.Net;

namespace W3ChampionsStatisticService.Admin
{
    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly IMatchRepository _matchRepository;
        private readonly MatchmakingServiceClient _matchmakingServiceRepository;
        private readonly INewsRepository _newsRepository;
        private readonly IInformationMessagesRepository _informationMessagesRepository;
        private readonly IAdminRepository _adminRepository;
        private readonly IRankRepository _rankRepository;

        public AdminController(
            IMatchRepository matchRepository,
            MatchmakingServiceClient matchmakingServiceRepository,
            INewsRepository newsRepository,
            IInformationMessagesRepository informationMessagesRepository,
            IAdminRepository adminRepository,
            IRankRepository rankRepository)
        {
            _matchRepository = matchRepository;
            _matchmakingServiceRepository = matchmakingServiceRepository;
            _newsRepository = newsRepository;
            _informationMessagesRepository = informationMessagesRepository;
            _adminRepository = adminRepository;
            _rankRepository = rankRepository;
        }

        [HttpGet("health-check")]
        public IActionResult HealthCheck()
        {
            return Ok();
        }

        [HttpGet("db-health-check")]
        public async Task<IActionResult> DatabaseHealthCheck()
        {
            var countOnGoingMatches = await _matchRepository.Count();
            return Ok(countOnGoingMatches);
        }

        [HttpGet("bannedPlayers")]
        public async Task<IActionResult> GetBannedPlayers()
        {
            var bannedPlayers = await _matchmakingServiceRepository.GetBannedPlayers();
            return Ok(bannedPlayers);
        }

        [HttpPost("bannedPlayers")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> PostBannedPlayer([FromBody] BannedPlayerReadmodel bannedPlayerReadmodel)
        {
            var bannedPlayers = await _matchmakingServiceRepository.PostBannedPlayer(bannedPlayerReadmodel);
            return Ok(bannedPlayers);
        }

        [HttpDelete("bannedPlayers")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> DeleteBannedPlayer([FromBody] BannedPlayerReadmodel bannedPlayerReadmodel)
        {
            var bannedPlayers = await _matchmakingServiceRepository.DeleteBannedPlayer(bannedPlayerReadmodel);
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

        [HttpGet("loadingScreenTips")]
        public async Task<IActionResult> GetTips(int? limit)
        {
            return Ok(await _informationMessagesRepository.GetTips(limit));
        }
        
        [HttpGet("motd")] // Message Of The Day
        public async Task<IActionResult> GetMotd()
        {
            return Ok(await _informationMessagesRepository.GetMotd());
        }

        [HttpPut("motd")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> SetMotd([FromBody] MessageOfTheDay motd)
        {
            if (motd.motd.Length > 400)
            {
                return new BadRequestObjectResult("The motd exceeded 400 characters. We can't display messages this long!");
            }

            await _informationMessagesRepository.SetMotd(motd);
            return Ok();
        }

        [HttpGet("loadingScreenTips/randomTip")]
        public async Task<IActionResult> GetRandomTip()
        {
            return Ok(await _informationMessagesRepository.GetRandomTip());
        }

        [HttpPut("loadingScreenTips/{tipId}")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> UpdateTips(string tipId, [FromBody] LoadingScreenTip loadingScreenTip)
        {
            if (loadingScreenTip.Message.Length > 200)
            {
                return new BadRequestObjectResult("The tip exceeded 200 characters. We can't display messages this long!");
            }
            loadingScreenTip.Id = new ObjectId(tipId);
            await _informationMessagesRepository.UpsertTip(loadingScreenTip);
            return Ok();
        }

        [HttpPut("loadingScreenTips")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> UpdateTips([FromBody] LoadingScreenTip loadingScreenTip)
        {
            if (loadingScreenTip.Message.Length > 200)
            {
                return new BadRequestObjectResult("The tip exceeded 200 characters. We can't display messages this long!");
            }
            loadingScreenTip.Id = ObjectId.GenerateNewId();
            await _informationMessagesRepository.UpsertTip(loadingScreenTip);
            return Ok();
        }

        [HttpDelete("loadingScreenTips/{tipId}")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> DeleteTip(string tipId)
        {
            await _informationMessagesRepository.DeleteTip(new ObjectId(tipId));
            return Ok();
        }

        [HttpGet("queue-data")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> GetQueueData()
        {
            var queueData = await _matchmakingServiceRepository.GetLiveQueueData();
            return Ok(queueData);
        }

        [HttpGet("proxies")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> GetProxies()
        {
            return Ok(await _adminRepository.GetProxies());
        }

        [HttpGet("proxies-for/{tag}")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> GetProxiesFor([FromRoute] string tag)
        {
            return Ok(await _adminRepository.GetProxiesFor(tag));
        }

        [HttpPut("update-proxies/{tag}")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> UpdateProxies([FromBody] ProxyUpdate proxyUpdateData, [FromRoute] string tag)
        {
            await _adminRepository.UpdateProxies(proxyUpdateData, tag);
            return Ok();
        }

        [HttpGet("search/{tagSearch}")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> SearchPlayer([FromRoute] string tagSearch)
        {
            var playerInstances = await _rankRepository.SearchAllPlayersForProxy(tagSearch);
            return Ok(playerInstances);
        }

        [HttpGet("alts/{tag}")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> SearchSmurfs([FromRoute] string tag)
        {
            var smurfs = await _adminRepository.SearchSmurfsFor(tag);
            return Ok(smurfs);
        }

        [HttpGet("globalChatBans")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> SearchChatbans()
        {
            var chatBans = await _adminRepository.GetChatBans();
            return Ok(chatBans);
        }

        [HttpPut("globalChatBans")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> PutChatBan([FromBody] ChatBanPutDto chatBan)
        {
            await _adminRepository.PutChatBan(chatBan);
            return Ok();
        }

        [HttpDelete("globalChatBans/{id}")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> DeleteChatBan([FromRoute] string id)
        {
            await _adminRepository.DeleteChatBan(id);
            return Ok();
        }
    }
}