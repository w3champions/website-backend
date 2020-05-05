using System.Collections.Generic;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.PadEvents.FakeEventSync
{
    public class TempRemainingWins
    {
        public TempRemainingWins(string id, List<RaceAndWinDto> remainingWins)
        {
            Id = id;
            RemainingWins = remainingWins;
        }
        
        public string Id { get; set; }
        public List<RaceAndWinDto> RemainingWins { get; set; }
    }

    public class TempRemainingLosses : TempRemainingWins
    {
        public TempRemainingLosses(string id, List<RaceAndWinDto> remainingWins) : base(id, remainingWins)
        {
        }

        public static TempRemainingLosses Create(string playerId)
        {
            return new TempRemainingLosses(playerId, new List<RaceAndWinDto>
            {
                new RaceAndWinDto(Race.HU, 0),
                new RaceAndWinDto(Race.OC, 0),
                new RaceAndWinDto(Race.NE, 0),
                new RaceAndWinDto(Race.UD, 0),
                new RaceAndWinDto(Race.RnD, 0),
            });
        }
    }


}