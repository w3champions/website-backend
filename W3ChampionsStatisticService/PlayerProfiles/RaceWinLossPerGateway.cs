using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class RaceWinLossPerGateway : WinLoss
    {
        public RaceWinLossPerGateway(Race race, GateWay gateWay, int season)
        {
            Race = race;
            GateWay = gateWay;
            Season = season;
        }

        public Race Race { get; set; }
        public GateWay GateWay { get; set; }
        public int Season { get; set; }
    }
}