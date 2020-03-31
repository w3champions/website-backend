using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PlayerRaceLossRatios
{
    public class PlayerRaceLossRatio
    {
        public static PlayerRaceLossRatio Create(string battleTag)
        {
            return new PlayerRaceLossRatio
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