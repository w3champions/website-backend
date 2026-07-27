using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Ladder;

[BsonIgnoreExtraElements]
public class Rank : IIdentifiable
{
    public Rank(
        List<string> playerIds,
        int league,
        int rankNumber,
        double rankingPoints,
        Race? race,
        GateWay gateway,
        GameMode gameMode,
        int season)
    {
        Gateway = gateway;
        League = league;
        RankNumber = rankNumber;
        RankingPoints = rankingPoints;
        Race = race;
        var btags = playerIds.Select(b => $"{b}@{(int)gateway}").OrderBy(t => t);
        var createPlayerId = $"{season}_{string.Join("_", btags)}_{gameMode}";
        if (race != null)
        {
            createPlayerId += $"_{race}";
        }
        PlayerId = createPlayerId;
        GameMode = gameMode;
        Season = season;

        Player1Id = playerIds.FirstOrDefault();
        Player2Id = playerIds.Skip(1).FirstOrDefault();
        MemberIds = playerIds;
    }

    public GateWay Gateway { get; set; }
    public string Id => PlayerId;
    public int League { get; set; }
    public int LeagueDivision { get; set; }
    public string LeagueName { get; set; }
    public int LeagueOrder { get; set; }
    public int RankNumber { get; set; }
    public double RankingPoints { get; set; }
    public Race? Race { get; set; }
    public string PlayerId { get; set; }

    // Every member of the ranked entry. Player1Id/Player2Id hold the first two of these; looking a
    // player up by battleTag goes through this list, so members past the second are findable too.
    // JsonIgnore keeps it out of API responses: it exists for the lookup, and serializing it would
    // change the wire shape of every legacy endpoint that returns ranks.
    [JsonIgnore]
    public List<string> MemberIds { get; set; } = [];
    public string Player1Id { get; set; }
    public string Player2Id { get; set; }
    [JsonIgnore]
    public List<PlayerOverview> Players { get; set; }
    public PlayerOverview Player => Players?.SingleOrDefault();
    public GameMode GameMode { get; set; }
    public int Season { get; set; }

    [BsonIgnore]
    public List<PlayerInfo> PlayersInfo { get; set; } = new List<PlayerInfo>();
    [JsonIgnore]
    [BsonIgnoreIfNull]
    public List<PersonalSettings.PersonalSetting> PlayerSettings { get; set; } = new List<PersonalSettings.PersonalSetting>();
}
