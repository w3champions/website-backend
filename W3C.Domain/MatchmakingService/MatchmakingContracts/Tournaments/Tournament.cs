using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using W3C.Domain.CommonValueObjects;

namespace W3C.Domain.MatchmakingService.MatchmakingContracts.Tournaments
{
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

        // TODO: add this
        // public string MatcherinoLink { get; set; }
    }
}
