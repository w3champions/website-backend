namespace W3ChampionsStatisticService.Tournaments.TournamentResults
{
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