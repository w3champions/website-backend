using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Tournaments.Models;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Tournaments
{
    [ApiController]
    [Route("api/tournaments")]
    public class TournamentsController : ControllerBase
    {
        private readonly TournamentsRepository _tournamentsRepository;
        public TournamentsController(TournamentsRepository tournamentsRepository)
        {
            _tournamentsRepository = tournamentsRepository;
        }

        [HttpGet("")]
        public async Task<IActionResult> GetTournaments()
        {
            List<Tournament> result = await _tournamentsRepository.GetAll();

            return Ok(new { tournaments = result ?? new List<Tournament>() });
        }

        [HttpPut("{id}")]
        [CheckIfBattleTagIsAdmin]
        public async Task<IActionResult> UpdateTournament(string id, Tournament tournament)
        {
            tournament.Id = new MongoDB.Bson.ObjectId(id);
            await _tournamentsRepository.Save(tournament);

            return Ok(tournament);
        }
    }
}
