using System.Collections.Generic;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;

// Season-keyed downstream read-model of a player's / arranged-team's progression rank.
// One document per entity (atTeamId ?? battleTag), keyed via BattleTagIdCombined so prior
// seasons are retained automatically. Carries only the published rank fields below.
public class PlayerProgression : IIdentifiable
{
    public string Id { get; set; }
    public int Season { get; set; }
    public GateWay GateWay { get; set; }
    public GameMode GameMode { get; set; }
    public Race? Race { get; set; }
    public List<PlayerId> PlayerIds { get; set; }

    public int? League { get; set; }
    public int? Division { get; set; }
    public int? Points { get; set; }
    public int? ApexPoints { get; set; }

    public static PlayerProgression Create(BattleTagIdCombined id)
    {
        return new PlayerProgression
        {
            Id = id.Id,
            Season = id.Season,
            GateWay = id.GateWay,
            GameMode = id.GameMode,
            Race = id.Race,
            PlayerIds = id.BattleTags,
        };
    }

    public void RecordRank(int? league, int? division, int? points, int? apexPoints)
    {
        League = league;
        Division = division;
        Points = points;
        ApexPoints = apexPoints;
    }
}
