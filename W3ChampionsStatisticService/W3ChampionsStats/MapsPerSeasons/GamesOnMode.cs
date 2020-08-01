using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons
{
    public class GamesOnMode
    {
        public static GamesOnMode Create(GameMode gameMode)
        {
            return new GamesOnMode
            {
                GameMode = gameMode
            };
        }

        public void CountMatch()
        {
            Count++;
        }

        public int Count { get; set; }
        public GameMode GameMode { get; set; }
    }
}