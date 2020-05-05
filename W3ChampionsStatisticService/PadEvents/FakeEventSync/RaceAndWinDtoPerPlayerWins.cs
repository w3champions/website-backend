using System.Collections.Generic;

namespace W3ChampionsStatisticService.PadEvents.FakeEventSync
{
    public class RaceAndWinDtoPerPlayerWins
    {
        public RaceAndWinDtoPerPlayerWins(string id, List<RaceAndWinDto> remainingWins)
        {
            Id = id;
            RemainingWins = remainingWins;
        }
        
        public string Id { get; set; }
        public List<RaceAndWinDto> RemainingWins { get; set; }
    }

    public class RaceAndWinDtoPerPlayerLosses : RaceAndWinDtoPerPlayerWins
    {
        public RaceAndWinDtoPerPlayerLosses(string id, List<RaceAndWinDto> remainingWins) : base(id, remainingWins)
        {
        }
    }


}