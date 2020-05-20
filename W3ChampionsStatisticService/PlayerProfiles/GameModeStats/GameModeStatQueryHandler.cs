using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
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
            var player = await _playerRepository.LoadGameModeStatPerGateway(battleTag, gateWay, season);
            var leaguesOfPlayer = await _rankRepository.LoadPlayerOfLeague(battleTag);
            var allLeagues = await _rankRepository.LoadLeagueConstellation();

            PopulateStats(leaguesOfPlayer, player, allLeagues);
            return player;
        }

         //way to shitty, do this with better rm one day
        private void PopulateStats(List<Rank> leaguesOfPlayer,
            List<PlayerGameModeStatPerGateway> player,
            List<LeagueConstellation> allLeagues)
        {
            var gm1V1S = leaguesOfPlayer.Where(l => l.GameMode == GameMode.GM_1v1);
            foreach (var rank in gm1V1S)
            {
                PopulateLeague(player, allLeagues, rank);
            }

            var all2V2SGrouped = leaguesOfPlayer.Where(l => l.GameMode == GameMode.GM_2v2_AT).GroupBy(l => l.Season);
            foreach (var groupForOneSeason in all2V2SGrouped)
            {
                var highest2V2Ranking = groupForOneSeason.OrderBy(r => r.League).ThenByDescending(r => r.RankNumber).First();
                PopulateLeague(player, allLeagues, highest2V2Ranking);
            }
        }

        private void PopulateLeague(List<PlayerGameModeStatPerGateway> player, List<LeagueConstellation> allLeagues, Rank
         rank)
        {
            try
            {
                var leagueConstellation = allLeagues.Single(l => l.Gateway == rank.Gateway && l.Season == rank.Season && l.GameMode == rank.GameMode);
                var league = leagueConstellation.Leagues.Single(l => l.Id == rank.League);

                var gameModeStat = player.Single(g => g.Season == rank.Season && g.GateWay == rank.Gateway && g.GameMode == rank.GameMode);

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
    }
}