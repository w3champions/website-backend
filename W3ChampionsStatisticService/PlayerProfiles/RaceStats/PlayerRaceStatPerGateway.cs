using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerProfiles.RaceStats
{
    public class PlayerRaceStatPerGateway : WinLoss, IIdentifiable
    {
        public PlayerRaceStatPerGateway(string battleTag, Race race, GateWay gateWay, int season)
        {
            Id = $"{season}_{battleTag}_@{gateWay}_{race}";
            Race = race;
            GateWay = gateWay;
            Season = season;
        }

        public Race Race { get; set; }
        public GateWay GateWay { get; set; }
        public int Season { get; set; }
        public string Id { get; set; }
    }
}