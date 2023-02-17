﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.PlayerProfiles.GameModeStats
{
    public class GameModeStatQueryHandler
    {
        private readonly IPlayerRepository _playerRepository;
        private readonly TrackingService _trackingService;
        private readonly IRankRepository _rankRepository;

        public GameModeStatQueryHandler(
            IPlayerRepository playerRepository,
            TrackingService trackingService,
            IRankRepository rankRepository)
        {
            _playerRepository = playerRepository;
            _trackingService = trackingService;
            _rankRepository = rankRepository;
        }

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
                _trackingService.TrackException(e, $"A League was not found for {rank.Id} RN: {rank.RankNumber} LE:{rank.League}");
            }
        }

        private async Task PopulateQuantilesAsync(List<PlayerGameModeStatPerGateway> playerGameModeStats, int season)
        {
            foreach (var gameModeStat in playerGameModeStats)
            {
                gameModeStat.Quantile =  await _playerRepository.GetQuantileForPlayer(gameModeStat.PlayerIds, gameModeStat.GateWay, gameModeStat.GameMode, gameModeStat.Race, season);
            }
        }
    }
}