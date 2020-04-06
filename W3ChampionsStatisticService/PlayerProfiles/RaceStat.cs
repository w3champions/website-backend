using W3ChampionsStatisticService.PlayerStats;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class RaceStat
    {
        public RaceStat(Race race)
        {
            Race = race;
        }

        public Race Race { set; get; }
        public int Wins { set; get; }
        public int Losses { set; get; }
        public double Winrate => new WinRate(Wins, Losses).Rate;

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