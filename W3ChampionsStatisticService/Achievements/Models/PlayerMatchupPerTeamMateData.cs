namespace W3ChampionsStatisticService.Achievements.Models  {
    class PlayerAndTeamMateRecordData {
        public long NumberOfWins { get; set; }
        public long NumberOfLosses { get; set; }

        // has to be estimated, what if partner left and the game continued?
        public long EstimatedGameTimeTogether { get; set; }
    }
}