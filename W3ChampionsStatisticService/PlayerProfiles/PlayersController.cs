using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.PlayerProfiles.War3InfoPlayerAkas;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    [ApiController]
    [Route("api/players")]
    public class PlayersController : ControllerBase
    {
        private readonly IPlayerRepository _playerRepository;
        private readonly GameModeStatQueryHandler _queryHandler;
        private readonly IPersonalSettingsRepository _personalSettingsRepository;
        private readonly IClanRepository _clanRepository;
        private readonly IW3CAuthenticationService _authenticationService;
        private readonly PlayerAkaProvider _playerAkaProvider;
        private readonly PersonalSettingsProvider _personalSettingsProvider;

        public PlayersController(
            IPlayerRepository playerRepository,
            GameModeStatQueryHandler queryHandler,
            IPersonalSettingsRepository personalSettingsRepository,
            IClanRepository clanRepository,
            IW3CAuthenticationService authenticationService,
            PlayerAkaProvider playerAkaProvider,
            PersonalSettingsProvider personalSettingsProvider)
        {
            _playerRepository = playerRepository;
            _queryHandler = queryHandler;
            _personalSettingsRepository = personalSettingsRepository;
            _clanRepository = clanRepository;
            _authenticationService = authenticationService;
            _playerAkaProvider = playerAkaProvider;
            _personalSettingsProvider = personalSettingsProvider;
        }

        [HttpGet("{battleTag}")]
        public async Task<IActionResult> GetPlayer([FromRoute] string battleTag, [FromQuery] string authorization)
        {
            var player = await _playerRepository.LoadPlayerProfile(battleTag);
            if (player == null && authorization != null)
            {
                var user = _authenticationService.GetUserByToken(authorization);
                if (user == null)
                {
                    return Unauthorized("Sorry Hackerboi");
                }
                
                player = PlayerOverallStats.Create(battleTag);
            }

            var cachedSettings = _personalSettingsProvider
                .GetPersonalSettings()
                .First(x => x.Id == battleTag);
            if (cachedSettings != null && cachedSettings.IsExcluded) // for players who request profile deletion
            {
                return NotFound();
            }
            
            // Akas are stored in cache - preferences for showing akas are stored in DB
            var settings = await _personalSettingsRepository.Load(battleTag);
            player.PlayerAkaData = _playerAkaProvider.GetAkaDataByPreferences(battleTag, settings);

            await _playerRepository.UpsertPlayer(player);
            
            return Ok(player);
        }

        [HttpGet("{battleTag}/clan-and-picture")]
        [Obsolete("Should be removed when correct sync is done")]
        public async Task<IActionResult> GetPlayer([FromRoute] string battleTag)
        {
            var playersClan = await _clanRepository.LoadMemberShip(battleTag);
            var settings = await _personalSettingsRepository.Load(battleTag);

            return Ok(new ChatDetailsDto(playersClan?.ClanId, settings?.ProfilePicture));
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
        public IActionResult GetPlayerAka([FromRoute] string battleTag)
        {
            var player = _playerAkaProvider.GetPlayerAkaData(battleTag.ToLower());
            return Ok(player);
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
}