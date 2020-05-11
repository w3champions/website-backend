using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class PlayerQueryHandler
    {
        private readonly IPlayerRepository _playerRepository;
        private readonly IRankRepository _rankRepository;

        public PlayerQueryHandler(
            IPlayerRepository playerRepository,
            IRankRepository rankRepository)
        {
            _playerRepository = playerRepository;
            _rankRepository = rankRepository;
        }
        public async Task<PlayerProfile> LoadPlayerWithRanks(string battleTag, int season)
        {
            var player = await _playerRepository.LoadPlayer(battleTag) ?? PlayerProfile.Default();
            var leaguesOfPlayer = await _rankRepository.LoadPlayerOfLeague(battleTag, season);
            var allLeagues = await _rankRepository.LoadLeagueConstellation(season);

            PopulateStats(leaguesOfPlayer, player, allLeagues, GameMode.GM_1v1, GateWay.Europe);
            PopulateStats(leaguesOfPlayer, player, allLeagues, GameMode.GM_1v1, GateWay.Usa);
            PopulateStats(leaguesOfPlayer, player, allLeagues, GameMode.GM_2v2_AT, GateWay.Europe);
            PopulateStats(leaguesOfPlayer, player, allLeagues, GameMode.GM_2v2_AT, GateWay.Usa);
            return player;
        }

         //way to shitty, do this with better rm one day
        private void PopulateStats(
            List<Rank> leaguesOfPlayer,
            PlayerProfile player,
            List<LeagueConstellation> allLeagues,
            GameMode gameMode,
            GateWay gateWay)
        {
            var gameModeIndex = gameMode switch
            {
                GameMode.GM_1v1 => 0,
                GameMode.GM_2v2_AT => 1,
                _ => 0
            };

            var searchedLeagues = gameMode != GameMode.GM_2v2_AT
                ? leaguesOfPlayer.FirstOrDefault(l => l.GameMode == gameMode && l.Gateway == gateWay)
                : leaguesOfPlayer.OrderBy(l => l.League).ThenBy(l => l.RankNumber).FirstOrDefault(l => l.GameMode == gameMode && l.Gateway == gateWay);

            if (searchedLeagues != null)
            {
                player.GateWayStats
                    .Single(g => g.GateWay == gateWay)
                    .GameModeStats[gameModeIndex].Rank = searchedLeagues.RankNumber;
                player.GateWayStats
                    .Single(g => g.GateWay == gateWay)
                    .GameModeStats[gameModeIndex].LeagueId = searchedLeagues.League;
                player.GateWayStats
                    .Single(g => g.GateWay == gateWay)
                    .GameModeStats[gameModeIndex].LeagueOrder = allLeagues
                    .Single(l => l.Gateway == gateWay && l.GameMode == gameMode)
                    .Leagues
                    .SingleOrDefault(l => l.Id == searchedLeagues.League)?.Order ?? 6;
                player.GateWayStats
                    .Single(g => g.GateWay == gateWay)
                    .GameModeStats[gameModeIndex].Division = allLeagues
                    .Single(l => l.Gateway == gateWay && l.GameMode == gameMode)
                    .Leagues
                    .SingleOrDefault(l => l.Id == searchedLeagues.League)?.Division ?? 0;
            }
        }
    }
}