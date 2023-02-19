using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;
using W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;
using W3ChampionsStatisticService.PlayerProfiles.RaceStats;
using W3C.Contracts.Matchmaking;

namespace W3ChampionsStatisticService.Ports
{
    public interface IPlayerRepository
    {
        Task UpsertPlayer(PlayerOverallStats playerOverallStats);
        Task UpsertPlayerOverview(PlayerOverview playerOverview);
        Task<PlayerOverallStats> LoadPlayerProfile(string battleTag);
        Task<PlayerOverview> LoadOverview(string battleTag);
        Task<PlayerWinLoss> LoadPlayerWinrate(string playerId, int season);
        Task<List<PlayerDetails>> LoadPlayersRaceWins(List<string> playerIds);
        Task UpsertWins(List<PlayerWinLoss> winrate);
        Task<List<int>> LoadMmrs(int season, GateWay gateWay, GameMode gameMode);
        Task<List<PlayerOverallStats>> SearchForPlayer(string search);
        Task<PlayerGameModeStatPerGateway> LoadGameModeStatPerGateway(string id);
        Task UpsertPlayerGameModeStatPerGateway(PlayerGameModeStatPerGateway stat);
        Task<List<PlayerGameModeStatPerGateway>> LoadGameModeStatPerGateway(string battleTag, GateWay gateWay, int season);
        Task<List<PlayerRaceStatPerGateway>> LoadRaceStatPerGateway(string battleTag, GateWay gateWay, int season);
        Task<PlayerRaceStatPerGateway> LoadRaceStatPerGateway(string battleTag, Race race, GateWay gateWay, int season);
        Task UpsertPlayerRaceStat(PlayerRaceStatPerGateway stat);
        Task<PlayerMmrRpTimeline> LoadPlayerMmrRpTimeline(string battleTag, Race race, GateWay gateWay, int season, GameMode gameMode);
        Task UpsertPlayerMmrRpTimeline(PlayerMmrRpTimeline mmrRpTimeline);
        Task<List<PlayerOverview>> LoadOverviews(int season);
        Task<Dictionary<string, PlayerOverallStats>> GetPlayerBattleTagsAsync(ICollection<string> personalSettingIds);
    }
}