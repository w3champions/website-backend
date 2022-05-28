using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons
{
    public class MapsPerSeason : IIdentifiable
    {
        public List<MatchOnMapPerMode> MatchesOnMapPerModes { get; set; } = new List<MatchOnMapPerMode>();

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
            var matchOnMapOverall = MatchesOnMapPerModes.SingleOrDefault(m => m.GameMode == GameMode.Undefined);
            if (matchOnMapOverall == null)
            {
                MatchesOnMapPerModes.Add(MatchOnMapPerMode.Create(GameMode.Undefined));
            }

            matchOnMapOverall = MatchesOnMapPerModes.Single(m => m.GameMode == GameMode.Undefined);
            matchOnMapOverall.CountMatch(map);

            var matchOnMap = MatchesOnMapPerModes.SingleOrDefault(m => m.GameMode == gameMode);
            if (matchOnMap == null)
            {
                MatchesOnMapPerModes.Add(MatchOnMapPerMode.Create(gameMode));
            }

            matchOnMap = MatchesOnMapPerModes.Single(m => m.GameMode == gameMode);
            matchOnMap.CountMatch(map);
        }
    }
}