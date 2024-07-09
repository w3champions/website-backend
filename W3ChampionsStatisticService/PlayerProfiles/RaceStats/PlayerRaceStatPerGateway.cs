using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.Repositories;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.PlayerProfiles.RaceStats;

public class PlayerRaceStatPerGateway : WinLoss, IIdentifiable
{
    public PlayerRaceStatPerGateway(string battleTag, Race race, GateWay gateWay, int season)
    {
        Id = $"{season}_{battleTag}_@{gateWay}_{race}";
        Race = race;
        GateWay = gateWay;
        BattleTag = battleTag;
        Season = season;
    }

    public Race Race { get; set; }
    public GateWay GateWay { get; set; }
    public int Season { get; set; }
    public string Id { get; set; }
    public string BattleTag { get; set; }
}
