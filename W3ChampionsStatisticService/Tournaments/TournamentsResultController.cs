using System.Linq;
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
                if (tournament.First == battleTag)
                {
                    var participation = new PlayerTournamentParticipation(tournament.ObjectId, TournamentPlacement.First);
                    playerParticipation.ParticipatedIn.Add(participation);
                    continue;
                }
                
                if (tournament.Second == battleTag)
                {
                    var participation = new PlayerTournamentParticipation(tournament.ObjectId, TournamentPlacement.Second);
                    playerParticipation.ParticipatedIn.Add(participation);
                    continue;
                }
                
                if (tournament.Third == battleTag)
                {
                    var participation = new PlayerTournamentParticipation(tournament.ObjectId, TournamentPlacement.Third);
                    playerParticipation.ParticipatedIn.Add(participation);
                    continue;
                }
                
                if (tournament.Forth == battleTag)
                {
                    var participation = new PlayerTournamentParticipation(tournament.ObjectId, TournamentPlacement.Forth);
                    playerParticipation.ParticipatedIn.Add(participation);
                    continue;
                }
                
                if (tournament.ThirdAndForth?.Contains(battleTag) == true)
                {
                    var participation = new PlayerTournamentParticipation(tournament.ObjectId, TournamentPlacement.ThirdAndForth);
                    playerParticipation.ParticipatedIn.Add(participation);
                    continue;
                }

                if (tournament.Participants?.Contains(battleTag) == true)
                {
                    var participation = new PlayerTournamentParticipation(tournament.ObjectId, TournamentPlacement.Participated);
                    playerParticipation.ParticipatedIn.Add(participation);
                }
            }

            return Ok(playerParticipation);
        }
    }
}
