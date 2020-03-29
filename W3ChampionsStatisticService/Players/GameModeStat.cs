using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.Players
{
    public class GameModeStat
    {
        public GameModeStat(GameMode gameMode)
        {
            Mode = gameMode;
        }

        public GameMode Mode { set; get; }
        public int Wins { set; get; }
        public int Losses { set; get; }

        public void Update(bool won)
        {
            if (won)
            {
                Wins++;
            }
            else
            {
                Losses++;
            }
        }
    }
}