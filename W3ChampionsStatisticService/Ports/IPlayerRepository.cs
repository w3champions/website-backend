using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;

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
        Task<List<string>> LoadAllIds();
        Task<PlayerRaceWins> LoadPlayerRaceWins(string battleTag);
        Task UpsertPlayerRaceWin(PlayerRaceWins player);
    }
}