namespace W3ChampionsStatisticService.W3ChampionsStats.GameLengths
{
    public class GameLength
    {
        public void AddGame()
        {
            Games++;
        }

        public long passedTimeInSeconds { get; set; }
        public long Games { get; set; }
    }
}