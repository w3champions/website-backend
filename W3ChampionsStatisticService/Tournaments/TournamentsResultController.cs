using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Tournaments.TournamentResults;

namespace W3ChampionsStatisticService.Tournaments
{
    [ApiController]
    [Route("api/tournament-result")]
    public class TournamentsResultController : ControllerBase
    {
        private readonly TournamentsRepository _tournamentsRepository;
        public TournamentsResultController(TournamentsRepository tournamentsRepository)
        {
            _tournamentsRepository = tournamentsRepository;
        }

        [HttpGet("{battleTag}")]
        public async Task<IActionResult> GetTournamentsForPlayer(string battleTag)
        {
            var tournaments = await _tournamentsRepository.GetAll();

            var playerParticipation = new PlayerParticipation(battleTag);
            foreach (var tournament in tournaments)
            {
                foreach (var loserBracketRound in tournament.LoserBracketRounds)
                {
                    foreach (var tournamentMatch in loserBracketRound.Matches)
                    {
                    }
                }

                var participation = new PlayerTournamentParticipation(tournament.ObjectId, TournamentPlacement.First);
                playerParticipation.ParticipatedIn.Add(participation);
            }

            return Ok(playerParticipation);
        }
    }
}
