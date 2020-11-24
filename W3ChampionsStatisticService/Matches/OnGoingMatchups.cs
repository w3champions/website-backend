using System.Collections.Generic;

namespace W3ChampionsStatisticService.Matches
{
    public static class OnGoingMatchUps
    {
        static OnGoingMatchUps()
        {
        }

        public static List<OnGoingMatchup> Instance { get; } = new List<OnGoingMatchup>();
    }
}