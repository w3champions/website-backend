using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;
using W3C.Domain.GameModes;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;
using Serilog;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.PlayerProfiles.GameModeStats;

[Trace]
public class GameModeStatQueryHandler(
    IPlayerRepository playerRepository,
    PlayerService playerService,
    ITrackingService trackingService,
    IRankRepository rankRepository,
    ProgressionViewLoader progressionViewLoader,
    MilestoneViewLoader milestoneViewLoader)
{
    private readonly IPlayerRepository _playerRepository = playerRepository;
    private readonly PlayerService _playerService = playerService;
    private readonly ITrackingService _trackingService = trackingService;
    private readonly IRankRepository _rankRepository = rankRepository;
    private readonly ProgressionViewLoader _progressionViewLoader = progressionViewLoader;
    private readonly MilestoneViewLoader _milestoneViewLoader = milestoneViewLoader;

    public async Task<List<PlayerGameModeStatPerGateway>> LoadPlayerStatsWithRanks(
        string battleTag,
        GateWay gateWay,
        int season)
    {
        var playerGameModeStats = await _playerRepository.LoadGameModeStatPerGateway(battleTag, gateWay, season);
        var leaguesOfPlayer = await _rankRepository.LoadPlayerOfLeague(battleTag, season);
        var allLeagues = await _rankRepository.LoadLeagueConstellation(season);

        foreach (var rank in leaguesOfPlayer)
        {
            PopulateLeague(playerGameModeStats, allLeagues, rank);
        }

        await PopulateQuantilesAsync(playerGameModeStats, season);
        await PopulateProgression(playerGameModeStats);
        await PopulateMilestone(playerGameModeStats);

        return playerGameModeStats.OrderByDescending(r => r.RankingPoints).ToList();
    }

    private void PopulateLeague(
        List<PlayerGameModeStatPerGateway> player,
        List<LeagueConstellation> allLeagues,
        Rank rank)
    {
        try
        {
            if (rank.RankNumber == 0) return;
            var leagueConstellation = allLeagues.Single(l => l.Gateway == rank.Gateway && l.Season == rank.Season && l.GameMode == rank.GameMode);

            // There are some Ranks with Leagues that do not exist in
            // Season 0 LeagueConstellations, which we should ignore.
            // (Data integrity issue)
            var league = leagueConstellation.Season == 0
                ? leagueConstellation.Leagues.SingleOrDefault(l => l.Id == rank.League)
                : leagueConstellation.Leagues.Single(l => l.Id == rank.League);

            if (league == null) return;


            var gameModeStat = player.SingleOrDefault(g => g.Id == rank.Id);

            if (gameModeStat == null) return;


            gameModeStat.Division = league.Division;
            gameModeStat.LeagueOrder = league.Order;

            gameModeStat.RankingPoints = rank.RankingPoints;
            gameModeStat.LeagueId = rank.League;
            gameModeStat.Rank = rank.RankNumber;
        }
        catch (Exception e)
        {
            _trackingService.TrackException(e, $"A League was not found for {rank.Id} - RankNumber: {rank.RankNumber} - League: {rank.League} - Message: {e.Message}");
            Log.Error(e, $"A League was not found for {rank.Id} RankNumber: {rank.RankNumber} League: {rank.League} {e.Message}");
        }
    }

    private async Task PopulateQuantilesAsync(List<PlayerGameModeStatPerGateway> playerGameModeStats, int season)
    {
        foreach (var gameModeStat in playerGameModeStats)
        {
            gameModeStat.Quantile = await _playerService.GetQuantileForPlayer(gameModeStat.PlayerIds, gameModeStat.GateWay, gameModeStat.GameMode, gameModeStat.Race, season);
        }
    }

    private async Task PopulateProgression(List<PlayerGameModeStatPerGateway> stats)
    {
        var views = await _progressionViewLoader.LoadViews(stats.Select(s => s.Id).ToList());
        foreach (var stat in stats)
        {
            stat.Progression = views.GetValueOrDefault(stat.Id);
        }
    }

    private async Task PopulateMilestone(List<PlayerGameModeStatPerGateway> stats)
    {
        // The milestone store is season-LESS, so stat.Id (season-prefixed) cannot be reused. Rebuild the
        // milestone key from the stat's components using the same path the ingest handler used: the stored
        // gameMode is already the arranged-team variant, and the race is included only for race-split modes
        // (in all seasons, matching the ingest rule — not the season-gated ladder rule).
        var idByStat = stats.ToDictionary(
            s => s,
            s => ProgressionMilestone.BuildId(
                s.PlayerIds,
                s.GateWay,
                s.GameMode,
                GameModesHelper.IsRaceSplitGameMode(s.GameMode) ? s.Race : null));

        var views = await _milestoneViewLoader.LoadViews(idByStat.Values.Distinct().ToList());
        foreach (var stat in stats)
        {
            stat.Milestone = views.GetValueOrDefault(idByStat[stat]);
        }
    }
}
