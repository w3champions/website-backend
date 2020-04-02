using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PlayerStats.RaceVersusRaceStats
{
    public class RaceVersusRaceRatio
    {
        public static RaceVersusRaceRatio Create(string battleTag)
        {
            return new RaceVersusRaceRatio
            {
                Id = battleTag
            };
        }

        public RaceWinRatio RaceWinRatio { get; set; } = RaceWinRatio.CreateRaceBased();
        public string Id { get; set; }

        public void AddRaceWin(bool won, Race myRace, Race enemyRace)
        {
            RaceWinRatio[myRace.ToString()][enemyRace.ToString()].RecordWin(won);
        }
    }
}