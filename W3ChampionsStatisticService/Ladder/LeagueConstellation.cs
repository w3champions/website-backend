using System.Collections.Generic;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Ladder;

public class LeagueConstellation : IIdentifiable
{
    public LeagueConstellation(int season, GateWay gateway, GameMode gameMode, List<League> leagues)
    {
        Id = $"{season}_{gateway}_{gameMode}";
        Season = season;
        Gateway = gateway;
        GameMode = gameMode;
        Leagues = leagues;
    }
    public string Id { get; set; }
    public int Season { get; set; }
    public GateWay Gateway { get; set; }
    public GameMode GameMode { get; set; }
    public List<League> Leagues { get; set; }
}
