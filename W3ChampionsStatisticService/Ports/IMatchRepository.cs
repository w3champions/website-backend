using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.MatchmakingService;
using W3ChampionsStatisticService.Heroes;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.Ports;

public interface IMatchRepository
{
    Task<List<Matchup>> Load(int season, GameMode gameMode, int offset = 0, int pageSize = 100, HeroType hero = HeroType.AllFilter, int minMmr = 0, int maxMmr = 3000);

    Task<long> Count(
        int season,
        GameMode gameMode,
        HeroType hero = HeroType.AllFilter,
        int minMmr = 0,
        int maxMmr = 3000);

    Task Insert(Matchup matchup);

    Task<List<Matchup>> LoadFor(string playerId,
        string opponentId = null,
        GateWay gateWay = GateWay.Undefined,
        GameMode gameMode = GameMode.Undefined,
        Race playerRace = Race.Total,
        Race opponentRace = Race.Total,
        int pageSize = 100,
        int offset = 0,
        int season = 1,
        HeroType hero = HeroType.AllFilter);
    Task<long> CountFor(string playerId,
        string opponentId = null,
        GateWay gateWay = GateWay.Undefined,
        GameMode gameMode = GameMode.Undefined,
        Race playerRace = Race.Total,
        Race opponentRace = Race.Total,
        int season = 1,
        HeroType hero = HeroType.AllFilter);

    Task<MatchupDetail> LoadFinishedMatchDetails(ObjectId id);
    Task<MatchupDetail> LoadFinishedMatchDetailsByMatchId(string id);
    Task<MatchFinishedEvent> LoadMatchFinishedEventByGameName(string gameName);

    Task InsertOnGoingMatch(OnGoingMatchup matchup);
    Task<OnGoingMatchup> LoadOnGoingMatchByMatchId(string matchId);
    Task<OnGoingMatchup> LoadOnGoingMatchForPlayer(string playerId);
    Task<OnGoingMatchup> TryLoadOnGoingMatchForPlayer(string playerId);
    Task DeleteOnGoingMatch(Matchup matchup);

    Task<List<OnGoingMatchup>> LoadOnGoingMatches(
        GameMode gameMode = GameMode.Undefined,
        GateWay gateWay = GateWay.Undefined,
        int offset = 0,
        int pageSize = 100,
        string map = "Overall",
        int minMmr = 0,
        int maxMmr = 3000,
        string sort = "startTimeDescending");

    Task<long> CountOnGoingMatches(
        GameMode gameMode = GameMode.Undefined,
        GateWay gateWay = GateWay.Undefined,
        string map = "Overall",
        int minMmr = 0,
        int maxMmr = 3000);

    Task EnsureIndices();

    Task<int> GetFloIdFromId(string gameId);
    Task<Season> LoadLastSeason();

    Task<DateTimeOffset?> AddPlayerHeroes(DateTimeOffset startTime, int pageSize);
}

public class MatchupDetail
{
    public Matchup Match { get; set; }
    public List<PlayerScore> PlayerScores { get; set; }
}
