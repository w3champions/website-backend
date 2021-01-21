using System.Collections.Generic;

namespace W3ChampionsStatisticService.Tournaments.TournamentResults
{
    public class PlayerParticipation
    {
        public string BattleTag { get; }
        public List<PlayerTournamentParticipation> ParticipatedIn = new List<PlayerTournamentParticipation>();

        public PlayerParticipation(string battleTag)
        {
            BattleTag = battleTag;
        }
    }
}