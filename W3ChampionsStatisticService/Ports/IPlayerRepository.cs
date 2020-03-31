using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerMapAndRaceRatios;
using W3ChampionsStatisticService.PlayerMapRatios;
using W3ChampionsStatisticService.PlayerOverviews;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerRaceLossRatios;

namespace W3ChampionsStatisticService.Ports
{
    public interface IPlayerRepository
    {
        Task UpsertPlayer(PlayerProfile playerProfile);
        Task UpsertPlayer(PlayerOverview playerOverview);
        Task<PlayerProfile> Load(string battleTag);
        Task<PlayerOverview> LoadOverview(string battleTag);
        Task<PlayerRaceLossRatio> LoadRaceStat(string battleTag);
        Task UpsertRaceStat(PlayerRaceLossRatio playerRaceLossRatio);
        Task<PlayerMapRatio> LoadMapStat(string battleTag);
        Task UpsertMapStat(PlayerMapRatio playerMapRatio);
        Task<PlayerMapAndRaceRatio> LoadMapAndRaceStat(string battleTag);
        Task UpsertMapAndRaceStat(PlayerMapAndRaceRatio playerMapAndRaceRatio);
    }
}