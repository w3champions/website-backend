using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.Repositories;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.PlayerProfiles.RaceStats;

public class PlayerRaceStatPerGateway(string battleTag, Race race, GateWay gateWay, int season) : WinLoss, IIdentifiable
{
    public Race Race { get; set; } = race;
    public GateWay GateWay { get; set; } = gateWay;
    public int Season { get; set; } = season;
    public string Id { get; set; } = $"{season}_{battleTag}_@{gateWay}_{race}";
    public string BattleTag { get; set; } = battleTag;
}
