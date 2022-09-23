using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Domain.MatchmakingService;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Tournaments
{
    [ApiController]
    [Route("api/tournaments")]
    public class TournamentsController : ControllerBase
    {
        private readonly IPersonalSettingsRepository _personalSettingsRepository;
        private readonly MatchmakingServiceClient _matchmakingServiceRepository;

        public TournamentsController(
            MatchmakingServiceClient matchmakingServiceRepository,
            IPersonalSettingsRepository personalSettingsRepository
        )
        {
            _personalSettingsRepository = personalSettingsRepository;
            _matchmakingServiceRepository = matchmakingServiceRepository;
        }

        [HttpGet("")]
        public async Task<IActionResult> GetTournaments()
        {
            var tournaments = await _matchmakingServiceRepository.GetTournaments();
            return Ok(tournaments);
        }

        [HttpPost("")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> CreateTournament([FromBody] TournamentUpdateBody tournamentData)
        {
            var tournament = await _matchmakingServiceRepository.CreateTournament(tournamentData);
            return Ok(tournament);
        }

        [HttpGet("upcoming")]
        public async Task<IActionResult> GetUpcomingTournament([FromQuery] GateWay gateway)
        {
            var tournament = await _matchmakingServiceRepository.GetUpcomingTournament(gateway);
            return Ok(tournament);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTournament(string id)
        {
            var tournament = await _matchmakingServiceRepository.GetTournament(id);
            return Ok(tournament);
        }

        [HttpPatch("{id}")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> UpdateTournament(string id, [FromBody] TournamentUpdateBody updates)
        {
            var tournament = await _matchmakingServiceRepository.UpdateTournament(id, updates);
            return Ok(tournament);
        }

        [HttpPost("{id}/players")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> RegisterPlayer(string id, [FromBody] RegisterPlayerBody body)
        {
            var personalSetting = await _personalSettingsRepository.Load(body.BattleTag);
            var tournament = await _matchmakingServiceRepository.RegisterPlayer(id, body.BattleTag, body.Race, personalSetting.CountryCode);
            return Ok(tournament);
        }

        public class RegisterPlayerBody
        {
            public string BattleTag { get; set; }
            public Race Race { get; set; }
        }
    }
}
