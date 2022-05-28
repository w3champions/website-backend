using System.Collections.Generic;
using System.Linq;
using W3C.Domain.CommonValueObjects;

namespace W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons
{
    public class MatchOnMapPerMode
    {
        public static MatchOnMapPerMode Create(GameMode gameMode)
        {
            return new MatchOnMapPerMode
            {
                GameMode = gameMode
            };
        }

        public GameMode GameMode { get; set; }

        public void CountMatch(string map)
        {
            var gamesOnMode = Maps.SingleOrDefault(g => g.Map == map);
            if (gamesOnMode == null)
            {
                Maps.Add(GamesPlayedOnMap.Create(map));
            }

            gamesOnMode = Maps.Single(g => g.Map == map);
            Maps = Maps.OrderBy(m => m.Map).ToList();
            gamesOnMode.CountMatch();
        }

        public List<GamesPlayedOnMap> Maps { get; set; } = new List<GamesPlayedOnMap>();
    }
}