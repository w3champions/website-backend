using System.Collections.Generic;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Ladder;

public class LeagueConstellation(int season, GateWay gateway, GameMode gameMode, List<League> leagues) : IIdentifiable
{
    public string Id { get; set; } = $"{season}_{gateway}_{gameMode}";
    public int Season { get; set; } = season;
    public GateWay Gateway { get; set; } = gateway;
    public GameMode GameMode { get; set; } = gameMode;
    public List<League> Leagues { get; set; } = leagues;
}
