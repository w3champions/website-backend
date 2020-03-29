namespace W3ChampionsStatisticService.Players
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