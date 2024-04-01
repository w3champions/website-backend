using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace W3C.Contracts.Matchmaking.Tournaments;

public class Tournament
{
    [JsonIgnore]
    public string _Id { get; set; }
    public string Id => _Id.ToString();
    public string Name { get; set; }
    public GameMode Mode { get; set; }
    public TournamentFormat Format { get; set; }
    public TournamentType Type { get; set; }
    public GateWay Gateway { get; set; }
    public TournamentState State { get; set; }
    public DateTime StartDateTime { get; set; }
    public List<int> MapPool { get; set; }
    public TournamentPlayer Winner { get; set; }
    public List<TournamentRound> Rounds { get; set; }
    public List<TournamentPlayer> Players { get; set; }
    public List<TournamentPlayer> Admins { get; set; }
    public string MatcherinoUrl { get; set; }
    public int? RegistrationTimeMinutes { get; set; }
    public int? ReadyTimeSeconds { get; set; }
    public int? VetoTimeSeconds { get; set; }
    public int? ShowWinnerTimeHours { get; set; }
    public int MaxPlayers { get; set; }
}
