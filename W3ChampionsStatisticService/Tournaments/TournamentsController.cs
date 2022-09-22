using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Domain.MatchmakingService;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Tournaments
{
    [ApiController]
    [Route("api/tournaments")]
    public class TournamentsController : ControllerBase
    {
        private readonly TournamentsRepository _tournamentsRepository;
        private readonly MatchmakingServiceClient _matchmakingServiceRepository;

        public TournamentsController(
          TournamentsRepository tournamentsRepository,
          MatchmakingServiceClient matchmakingServiceRepository)
        {
            _tournamentsRepository = tournamentsRepository;
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
            var tournament = await _matchmakingServiceRepository.RegisterPlayer(id, body.BattleTag, body.Race);
            return Ok(tournament);
        }

        public class RegisterPlayerBody
        {
            public string BattleTag { get; set; }
            public Race Race { get; set; }
        }
    }
}
