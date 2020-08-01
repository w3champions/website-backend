using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons
{
    public class MapsPerSeason : IIdentifiable
    {
        public List<MatchOnMap> MatchesOnMap { get; set; } = new List<MatchOnMap>();

        [JsonIgnore]
        public string Id => Season.ToString();

        public static MapsPerSeason Create(int season)
        {
            return new MapsPerSeason
            {
                Season = season
            };
        }

        public int Season { get; set; }

        public void Count(string map, GameMode gameMode)
        {
            var matchOnMap = MatchesOnMap.SingleOrDefault(m => m.Map == map);
            if (matchOnMap == null)
            {
                MatchesOnMap.Add(MatchOnMap.Create(map));
            }

            matchOnMap = MatchesOnMap.Single(m => m.Map == map);
            matchOnMap.CountMatch(gameMode);
        }
    }
}