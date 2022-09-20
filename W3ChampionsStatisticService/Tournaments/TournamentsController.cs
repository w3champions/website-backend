using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Domain.MatchmakingService;

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

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTournaments(string id)
        {
            var tournament = await _matchmakingServiceRepository.GetTournament(id);
            return Ok(tournament);
        }

        // TODO: implement this
        // [HttpPut("{id}")]
        // [CheckIfBattleTagIsAdmin]
        // public async Task<IActionResult> UpdateTournament(string id, Tournament tournament)
        // {
        //     tournament.Id = new MongoDB.Bson.ObjectId(id);
        //     await _tournamentsRepository.Save(tournament);

        //     return Ok(tournament);
        // }
    }
}
