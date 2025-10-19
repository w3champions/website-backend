using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3C.Domain.Repositories;
using W3C.Domain.CommonValueObjects;
using W3C.Contracts.Matchmaking;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.Tracing;
using Serilog;
namespace W3ChampionsStatisticService.Admin;

[ApiController]
[Route("api/admin")]
[Trace]
public class AdminController(
    IMatchRepository matchRepository,
    MatchmakingServiceClient matchmakingServiceRepository,
    INewsRepository newsRepository,
    IInformationMessagesRepository informationMessagesRepository,
    IAdminRepository adminRepository,
    IRankRepository rankRepository) : ControllerBase
{
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly MatchmakingServiceClient _matchmakingServiceRepository = matchmakingServiceRepository;
    private readonly INewsRepository _newsRepository = newsRepository;
    private readonly IInformationMessagesRepository _informationMessagesRepository = informationMessagesRepository;
    private readonly IAdminRepository _adminRepository = adminRepository;
    private readonly IRankRepository _rankRepository = rankRepository;

    [HttpGet("health-check")]
    [NoTrace]
    public IActionResult HealthCheck()
    {
        return Ok();
    }

    [HttpGet("db-health-check")]
    [NoTrace]
    public async Task<IActionResult> DatabaseHealthCheck()
    {
        var ongoingMatches = await _matchRepository.LoadOnGoingMatches(
            GameMode.GM_1v1, GateWay.Europe);

        return Ok(ongoingMatches.Count);
    }

    [HttpGet("bannedPlayers")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> GetBannedPlayers([FromQuery] BannedPlayersGetRequest req)
    {
        var bannedPlayers = await _matchmakingServiceRepository.GetBannedPlayers(req);
        return Ok(bannedPlayers);
    }

    [HttpPost("bannedPlayers")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> PostBannedPlayer([FromBody] BannedPlayerReadmodel bannedPlayerReadmodel)
    {
        await _matchmakingServiceRepository.PostBannedPlayer(bannedPlayerReadmodel);
        return Ok();
    }

    [HttpPost("bannedPlayers/batch")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> GetBannedPlayersBatch([FromBody] BattleTagsBatchRequest request)
    {
        if (request?.BattleTags == null || !request.BattleTags.Any())
        {
            return BadRequest("BattleTags list cannot be empty");
        }

        if (request.BattleTags.Count > 100)
        {
            return BadRequest("Maximum 100 battleTags per request");
        }

        // Fetch banned players for each battleTag in parallel
        var tasks = request.BattleTags.Select(tag =>
            _matchmakingServiceRepository.GetBannedPlayers(new BannedPlayersGetRequest
            {
                Page = 1,
                ItemsPerPage = 100,
                Search = tag.Trim()
            })
        );

        var results = await Task.WhenAll(tasks);

        // Filter to exact matches only (case-insensitive)
        var allPlayers = results
            .SelectMany(r => r.players)
            .Where(p => request.BattleTags.Any(tag =>
                string.Equals(p.battleTag, tag, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return Ok(new BannedPlayerResponse
        {
            total = allPlayers.Count(),
            players = allPlayers
        });
    }

    [HttpGet("news")]
    public async Task<IActionResult> GetNews(int? limit)
    {
        return Ok(await _newsRepository.Get(limit));
    }

    [HttpPut("news/{newsId}")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> UpdateNews(string newsId, [FromBody] NewsMessage newsMessage)
    {
        newsMessage.Id = new ObjectId(newsId);
        await _newsRepository.UpsertNews(newsMessage);
        return Ok();
    }

    [HttpPut("news")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> UpdateNews([FromBody] NewsMessage newsMessage)
    {
        newsMessage.Id = ObjectId.GenerateNewId();
        await _newsRepository.UpsertNews(newsMessage);
        return Ok();
    }

    [HttpDelete("news/{newsId}")]
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
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
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
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
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
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
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
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
    [BearerHasPermissionFilter(Permission = EPermission.Content)]
    public async Task<IActionResult> DeleteTip(string tipId)
    {
        await _informationMessagesRepository.DeleteTip(new ObjectId(tipId));
        return Ok();
    }

    [HttpGet("queue-data")]
    [BearerHasPermissionFilter(Permission = EPermission.Queue)]
    public async Task<IActionResult> GetQueueData()
    {
        var queueData = await _matchmakingServiceRepository.GetLiveQueueData();
        return Ok(queueData);
    }

    [HttpGet("proxies")]
    [BearerHasPermissionFilter(Permission = EPermission.Proxies)]
    public async Task<IActionResult> GetProxies()
    {
        return Ok(await _adminRepository.GetProxies());
    }

    [HttpGet("proxies-for/{tag}")]
    [BearerHasPermissionFilter(Permission = EPermission.Proxies)]
    public async Task<IActionResult> GetProxiesFor([FromRoute] string tag)
    {
        return Ok(await _adminRepository.GetProxiesFor(tag));
    }

    [HttpPut("update-proxies/{tag}")]
    [BearerHasPermissionFilter(Permission = EPermission.Proxies)]
    public async Task<IActionResult> UpdateProxies([FromBody] ProxyUpdate proxyUpdateData, [FromRoute] string tag)
    {
        await _adminRepository.UpdateProxies(proxyUpdateData, tag);
        return Ok();
    }

    [HttpGet("search/{tagSearch}")]
    [BearerHasPermissionFilter(Permission = EPermission.Proxies)]
    public async Task<IActionResult> SearchPlayer([FromRoute] string tagSearch)
    {
        var playerInstances = await _rankRepository.SearchAllPlayersForProxy(tagSearch);
        return Ok(playerInstances);
    }

    [HttpGet("globalChatBans")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> SearchChatbans([FromQuery] string query, [FromQuery] string nextId)
    {
        var chatBans = await _adminRepository.GetChatBans(query, nextId);
        return Ok(chatBans);
    }

    [HttpPut("globalChatBans")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> PutChatBan([FromBody] ChatBanPutDto chatBan)
    {
        await _adminRepository.PutChatBan(chatBan);
        return Ok();
    }

    [HttpDelete("globalChatBans/{id}")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> DeleteChatBan([FromRoute] string id)
    {
        await _adminRepository.DeleteChatBan(id);
        return Ok();
    }

    [HttpPost("globalChatBans/batch")]
    [BearerHasPermissionFilter(Permission = EPermission.Moderation)]
    public async Task<IActionResult> GetGlobalChatBansBatch([FromBody] BattleTagsBatchRequest request)
    {
        if (request?.BattleTags == null || !request.BattleTags.Any())
        {
            return BadRequest("BattleTags list cannot be empty");
        }

        if (request.BattleTags.Count > 100)
        {
            return BadRequest("Maximum 100 battleTags per request");
        }

        // Fetch global chat bans for each battleTag in parallel
        var tasks = request.BattleTags.Select(tag =>
            _adminRepository.GetChatBans(tag.Trim(), null)
        );

        var results = await Task.WhenAll(tasks);

        // Filter to exact matches only (case-insensitive)
        var allBans = results
            .SelectMany(r => r.globalChatBans)
            .Where(b => request.BattleTags.Any(tag =>
                string.Equals(b.battleTag, tag, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return Ok(new GlobalChatBanResponse
        {
            globalChatBans = allBans,
            next_id = null // No pagination for batch queries
        });
    }

    // This API endpoint just runs the 'CheckIfBattleTagIsAdmin' filter which then checks the jwt lifetime.
    // Returns a 200 OK if it passes validation and a 401 Unauthorized if it's expired.
    [HttpGet("checkJwtLifetime")]
    [CheckIfBattleTagIsAdmin]
    [NoTrace]
    public IActionResult CheckJwtLifetime()
    {
        return Ok();
    }

    [HttpGet("smurf-detection/ignored-identifiers")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerAdministration)]
    public async Task<IActionResult> GetIgnoredIdentifiers([FromQuery] string type, [FromQuery] string continuationToken)
    {
        return Ok(await _adminRepository.GetIgnoredIdentifiers(type, continuationToken));
    }

    [HttpPost("smurf-detection/ignored-identifiers")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerAdministration)]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> AddIgnoredIdentifier([FromBody] SmurfDetection.AddIgnoredIdentifierDto addIgnoredIdentifierDto, string actingPlayer)
    {
        var persistedIgnoredIdentifier = await _adminRepository.AddIgnoredIdentifier(addIgnoredIdentifierDto.type, addIgnoredIdentifierDto.identifier, addIgnoredIdentifierDto.reason, actingPlayer);
        return Ok(persistedIgnoredIdentifier);
    }

    [HttpDelete("smurf-detection/ignored-identifiers/{id}")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerAdministration)]
    public async Task<IActionResult> DeleteIgnoredIdentifier([FromRoute] string id)
    {
        await _adminRepository.DeleteIgnoredIdentifier(id);
        return Ok();
    }

    [HttpGet("smurf-detection/possible-identifier-types")]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerAdministration)]
    public async Task<IActionResult> GetPossibleIdentifierTypes()
    {
        return Ok(await _adminRepository.GetPossibleIdentifierTypes());
    }

    [HttpGet("smurf-detection/query-smurfs")]
    [InjectActingPlayerAuthCode]
    [BearerHasPermissionFilter(Permission = EPermission.SmurfCheckerQuery)]
    public async Task<IActionResult> QuerySmurfs([FromQuery] string identifierType, [FromQuery] string identifier, [FromQuery] bool generateExplanation, [FromQuery] int iterationDepth)
    {
        var actingPlayerUser = InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(HttpContext);
        if (generateExplanation && !actingPlayerUser.Permissions.Contains(EPermission.SmurfCheckerQueryExplanation))
        {
            Log.Error("User {BattleTag} tried to generate an explanation for smurf detection but doesn't have the permission.", actingPlayerUser.BattleTag);
            return Unauthorized("You don't have permission to generate explanations for smurf detection.");
        }
        var smurfSearchResult = await _adminRepository.QuerySmurfsFor(identifierType, identifier, generateExplanation, iterationDepth);
        return Ok(smurfSearchResult);
    }
}
