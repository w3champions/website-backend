using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

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

    public enum TournamentPlacement
    {
        First, Second, Third, Forth, Participated
    }

    public class PlayerParticipation
    {
        public string BattleTag { get; }
        public List<PlayerTournamentParticipation> ParticipatedIn = new List<PlayerTournamentParticipation>();

        public PlayerParticipation(string battleTag)
        {
            BattleTag = battleTag;
        }
    }

    public class PlayerTournamentParticipation
    {
        public string TournamentId { get; }
        public TournamentPlacement Placement { get; }

        public PlayerTournamentParticipation(string tournamentId, TournamentPlacement placement)
        {
            TournamentId = tournamentId;
            Placement = placement;
        }
    }
}
