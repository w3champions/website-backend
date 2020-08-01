using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons
{
    public class MatchOnMap
    {
        public static MatchOnMap Create(string map)
        {
            return new MatchOnMap
            {
                Map = map
            };
        }

        public void CountMatch(GameMode gameMode)
        {
            var gamesOnMode = GamesOnModes.SingleOrDefault(g => g.GameMode == gameMode);
            if (gamesOnMode == null)
            {
                GamesOnModes.Add(GamesOnMode.Create(gameMode));
            }

            gamesOnMode = GamesOnModes.Single(g => g.GameMode == gameMode);
            gamesOnMode.CountMatch();

            var gamesOnModeOverall = GamesOnModes.SingleOrDefault(g => g.GameMode == GameMode.Undefined);
            if (gamesOnModeOverall == null)
            {
                GamesOnModes.Add(GamesOnMode.Create(GameMode.Undefined));
            }

            gamesOnModeOverall = GamesOnModes.Single(g => g.GameMode == GameMode.Undefined);
            gamesOnModeOverall.CountMatch();
        }

        public string Map { get; set; }
        public List<GamesOnMode> GamesOnModes { get; set; } = new List<GamesOnMode>();
    }
}