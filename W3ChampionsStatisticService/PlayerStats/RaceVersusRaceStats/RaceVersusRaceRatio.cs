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

        public RaceWinRatio RaceWinRatio { get; set; } = RaceWinRatio.Create();
        public string Id { get; set; }

        public void AddRaceWin(Race myRace, Race enemyRace, bool won)
        {
            RaceWinRatio.RecordWin(myRace, enemyRace, won);
        }
    }
}