using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.Services;
using W3C.Contracts.GameObjects;

namespace W3ChampionsStatisticService.PlayerProfiles;

[ApiController]
[Route("api/players")]
public class PlayersController(
    IPlayerRepository playerRepository,
    GameModeStatQueryHandler queryHandler,
    IPersonalSettingsRepository personalSettingsRepository,
    IClanRepository clanRepository,
    IW3CAuthenticationService authenticationService,
    PlayerAkaProvider playerAkaProvider,
    PlayerService playerService) : ControllerBase
{
    private readonly IPlayerRepository _playerRepository = playerRepository;
    private readonly GameModeStatQueryHandler _queryHandler = queryHandler;
    private readonly IPersonalSettingsRepository _personalSettingsRepository = personalSettingsRepository;
    private readonly IClanRepository _clanRepository = clanRepository;
    private readonly IW3CAuthenticationService _authenticationService = authenticationService;
    private readonly PlayerAkaProvider _playerAkaProvider = playerAkaProvider;
    private readonly PlayerService _playerService = playerService;

    [HttpGet("global-search")]
    public async Task<IActionResult> GlobalSearchPlayer(string search, string lastRelevanceId = "", int pageSize = 20)
    {
        if (pageSize > 20) pageSize = 20;
        var players = await _playerService.GlobalSearchForPlayer(search, lastRelevanceId, pageSize);
        return Ok(players);
    }

    [HttpGet("{battleTag}")]
    public async Task<IActionResult> GetPlayer([FromRoute] string battleTag)
    {
        PlayerOverallStats player = await _playerRepository.LoadPlayerOverallStats(battleTag);

        if (player == null)
        {
            return NotFound($"Player {battleTag} not found.");
        }

        // Akas are stored in cache - preferences for showing akas are stored in DB
        PersonalSetting settings = await _personalSettingsRepository.LoadOrCreate(battleTag);
        player.PlayerAkaData = await _playerAkaProvider.GetAkaDataByPreferencesAsync(battleTag, settings);

        await _playerRepository.UpsertPlayer(player);

        return Ok(player);
    }

    [HttpGet("clan-memberships")]
    public async Task<IActionResult> GetPlayerClanSince([FromRoute] DateTimeOffset from)
    {
        var playersClan = await _clanRepository.LoadMemberShipsSince(from);
        return Ok(playersClan.Select(c => new ClanMemberhipDto(c.BattleTag, c.ClanId, c.LastUpdated)));
    }

    [HttpGet("profile-pictures")]
    public async Task<IActionResult> GetPlayerPictureSince([FromRoute] DateTimeOffset from)
    {
        var settings = await _personalSettingsRepository.LoadSince(from);
        return Ok(settings.Select(s => new ProfilePictureDto(s.LastUpdated, s.ProfilePicture)));
    }

    [HttpGet]
    public async Task<IActionResult> SearchPlayer(string search)
    {
        var players = await _playerRepository.SearchForPlayer(search);
        return Ok(players);
    }

    [HttpGet("{battleTag}/winrate")]
    public async Task<IActionResult> GetPlayerWinrate([FromRoute] string battleTag, int season)
    {
        var wins = await _playerRepository.LoadPlayerWinrate(battleTag, season);
        return Ok(wins);
    }

    [HttpGet("{battleTag}/game-mode-stats")]
    public async Task<IActionResult> GetGameModeStats(
        [FromRoute] string battleTag,
        GateWay gateWay,
        int season)
    {
        var wins = await _queryHandler.LoadPlayerStatsWithRanks(battleTag, gateWay, season);
        return Ok(wins);
    }

    [HttpGet("{battleTag}/race-stats")]
    public async Task<IActionResult> GetRaceStats(
        [FromRoute] string battleTag,
        GateWay gateWay,
        int season)
    {
        var wins = await _playerRepository.LoadRaceStatPerGateway(battleTag, gateWay, season);
        var ordered = wins.OrderBy(s => s.Race).ToList();
        var firstPick = ordered.FirstOrDefault();
        if (firstPick?.Race != Race.RnD) return Ok(ordered);

        ordered.Remove(firstPick);
        ordered.Add(firstPick);


        return Ok(ordered);
    }

    [HttpGet("{battleTag}/mmr-rp-timeline")]
    public async Task<IActionResult> GetPlayerMmrRpTimeline(
        [FromRoute] string battleTag,
        Race race,
        GateWay gateWay,
        int season,
        GameMode gameMode)
    {
        var playerMmrRpTimeline = await _playerRepository.LoadPlayerMmrRpTimeline(battleTag, race, gateWay, season, gameMode);
        return Ok(playerMmrRpTimeline);
    }

    [HttpGet("{battleTag}/aka")]
    public async Task<IActionResult> GetPlayerAka([FromRoute] string battleTag)
    {
        var player = await _playerAkaProvider.GetPlayerAkaDataAsync(battleTag.ToLower());
        return Ok(player);
    }

    [HttpGet("{battleTag}/game-length-stats")]
    public async Task<IActionResult> GetPlayerGameLengthStats([FromRoute] string battleTag, int season)
    {
        var lengthStats = await _playerRepository.LoadGameLengthForPlayerStats(battleTag, season);
        return Ok(lengthStats);
    }

    // Called by Player-Service to get battleTag and profile picture. Returns null if player isn't found.
    [HttpGet("{battleTag}/user-brief")]
    public async Task<IActionResult> GetUserBrief([FromRoute] string battleTag)
    {
        PersonalSetting settings = await _personalSettingsRepository.Find(battleTag);
        if (settings == null)
        {
            return NotFound();
        }
        return Ok(new UserBrief(settings.Id, settings.ProfilePicture));
    }
}

public class ProfilePictureDto
{
    public DateTimeOffset LastUpdated { get; }
    public ProfilePicture ProfilePicture { get; }

    public ProfilePictureDto(DateTimeOffset lastUpdated, ProfilePicture profilePicture)
    {
        LastUpdated = lastUpdated;
        ProfilePicture = profilePicture;
    }
}

public class ClanMemberhipDto
{
    public string BattleTag { get; }
    public string ClanId { get; }
    public DateTimeOffset LastUpdated { get; }

    public ClanMemberhipDto(string battleTag, string clanId, in DateTimeOffset lastUpdated)
    {
        BattleTag = battleTag;
        ClanId = clanId;
        LastUpdated = lastUpdated;
    }
}

public class ChatDetailsDto
{
    public string ClanId { get; }
    public ProfilePicture ProfilePicture { get; }

    public ChatDetailsDto(string clanId, ProfilePicture profilePicture)
    {
        ClanId = clanId;
        ProfilePicture = profilePicture;
    }
}

public class UserBrief
{
    public string BattleTag { get; }
    public string Name { get; }
    public ProfilePicture ProfilePicture { get; }

    public UserBrief(string battleTag, ProfilePicture profilePicture)
    {
        BattleTag = battleTag;
        Name = battleTag.Split("#")[0];
        ProfilePicture = profilePicture;
    }
}
