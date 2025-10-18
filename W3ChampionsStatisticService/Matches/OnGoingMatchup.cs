using System;
using System.Collections.Generic;
using System.Linq;
using W3C.Domain.MatchmakingService;

namespace W3ChampionsStatisticService.Matches;

public class OnGoingMatchup : Matchup
{
    public static OnGoingMatchup Create(MatchStartedEvent matchStartedEvent)
    {
        var match = matchStartedEvent.match;

        var startTime = DateTimeOffset.FromUnixTimeMilliseconds(match.startTime);

        var result = new OnGoingMatchup()
        {
            Id = matchStartedEvent.Id,
            Map = new MapName(match.map).Name,
            MapId = match.mapId,
            MapName = match.mapName,
            MatchId = match.id,
            GateWay = match.gateway,
            GameMode = match.gameMode,
            StartTime = startTime,
        };

        result.SetServerInfo(match, match.gameMode);

        var teamGroups = SplitPlayersIntoTeams(match.players, match.gameMode);

        foreach (var team in teamGroups)
        {
            result.Teams.Add(CreateTeam(team.Value));
        }

        SetTeamPlayers(result);

        return result;
    }

    private static Team CreateTeam(IEnumerable<UnfinishedMatchPlayer> players)
    {
        var team = new Team();
        team.Players.AddRange(CreatePlayerArray(players));
        return team;
    }

    private static IEnumerable<PlayerOverviewMatches> CreatePlayerArray(IEnumerable<UnfinishedMatchPlayer> players)
    {
        return players.Select(w => new PlayerOverviewMatches
        {
            Name = w.battleTag.Split("#")[0],
            BattleTag = w.battleTag,
            OldMmr = (int)w.mmr.rating,
            OldMmrQuantile = w.quantiles?.quantile,
            OldRankDeviation = w.mmr.rd,
            Race = w.race,
            Location = w.country,
            Ranking = w.ranking,
        });
    }
}
