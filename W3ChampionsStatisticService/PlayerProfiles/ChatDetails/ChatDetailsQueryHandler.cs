using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Ports;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.PlayerProfiles.ChatDetails;

/// <summary>Result of the chat-profile enrichment: best current-season rank + games played.</summary>
public class ChatDetailsEnrichment(ChatRank rank, int gamesPlayed, int? season)
{
    /// <summary>Null = no rank this season (well-defined "unranked", not an error).</summary>
    public ChatRank Rank { get; } = rank;
    /// <summary>Total current-season ladder games across all gateways, modes and races. 0 = none.</summary>
    public int GamesPlayed { get; } = gamesPlayed;
    /// <summary>The season the values were resolved for; null only when no season exists at all.</summary>
    public int? Season { get; } = season;
}

[Trace]
public class ChatDetailsQueryHandler(
    IMatchRepository matchRepository,
    IRankRepository rankRepository,
    IPlayerRepository playerRepository)
{
    private readonly IMatchRepository _matchRepository = matchRepository;
    private readonly IRankRepository _rankRepository = rankRepository;
    private readonly IPlayerRepository _playerRepository = playerRepository;

    public async Task<ChatDetailsEnrichment> LoadEnrichment(string battleTag)
    {
        var lastSeason = await _matchRepository.LoadLastSeason();
        if (lastSeason == null) return new ChatDetailsEnrichment(null, 0, null);
        var season = lastSeason.Id;

        var stats = await _playerRepository.LoadGameModeStatPerGateway(battleTag, season);
        var gamesPlayed = stats.Sum(s => s.Games);

        var ranks = await _rankRepository.LoadRanksForPlayers(new List<string> { battleTag }, season);
        var constellations = await _rankRepository.LoadLeagueConstellation(season);

        return new ChatDetailsEnrichment(SelectBestRank(ranks, constellations), gamesPlayed, season);
    }

    // Best = lowest league Order (0 = highest league); ties broken by lowest RankNumber, then
    // GameMode, then GateWay, so the result is deterministic for a given player+season.
    // Mirrors GameModeStatQueryHandler.PopulateLeague's tolerance: RankNumber == 0 and ranks
    // whose league is missing from the season's LeagueConstellation are skipped.
    private static ChatRank SelectBestRank(List<Rank> ranks, List<LeagueConstellation> constellations)
    {
        var candidates = new List<(League League, Rank Rank)>();
        foreach (var rank in ranks)
        {
            if (rank.RankNumber == 0) continue;
            var constellation = constellations.SingleOrDefault(c =>
                c.Gateway == rank.Gateway && c.Season == rank.Season && c.GameMode == rank.GameMode);
            var league = constellation?.Leagues?.SingleOrDefault(l => l.Id == rank.League);
            if (league == null) continue;
            candidates.Add((league, rank));
        }

        var best = candidates
            .OrderBy(c => c.League.Order)
            .ThenBy(c => c.Rank.RankNumber)
            .ThenBy(c => (int)c.Rank.GameMode)
            .ThenBy(c => (int)c.Rank.Gateway)
            .FirstOrDefault();

        if (best.Rank == null) return null;

        return new ChatRank(
            best.League.Id,
            best.League.Name,
            best.League.Order,
            best.League.Division,
            best.Rank.RankNumber,
            best.Rank.GameMode,
            best.Rank.Gateway);
    }
}
