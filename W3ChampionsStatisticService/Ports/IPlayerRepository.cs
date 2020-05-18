using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerProfiles._2v2Stats;

namespace W3ChampionsStatisticService.Ports
{
    public interface IPlayerRepository
    {
        Task UpsertPlayer(PlayerProfile playerProfile);
        Task UpsertPlayerOverview(PlayerOverview playerOverview);
        Task<PlayerProfile> LoadPlayer(string battleTag);
        Task<PlayerOverview> LoadOverview(string battleTag);
        Task<PlayerWinLoss> LoadPlayerWinrate(string playerId, int season);
        Task UpsertWins(List<PlayerWinLoss> winrate);
        Task<List<int>> LoadMmrs(int season);
        Task<GameModeStatPerGateway> LoadGameModeStatPerGateway(string id);
        Task UpsertPlayerGameModeStatPerGateway(GameModeStatPerGateway stat);
        Task<List<GameModeStatPerGateway>> LoadPlayerGameModeStat(string battleTag, GameMode gameMode, GateWay gateWay, int season);
    }
}