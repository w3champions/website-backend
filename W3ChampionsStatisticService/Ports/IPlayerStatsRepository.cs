using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.PlayerStats.RaceVersusRaceStats;

namespace W3ChampionsStatisticService.Ports
{
    public interface IPlayerStatsRepository
    {
        Task<PlayerRaceLossRatio> LoadRaceStat(string battleTag);
        Task UpsertRaceStat(PlayerRaceLossRatio playerRaceLossRatio);
        Task<RaceOnMapRatio> LoadMapStat(string battleTag);
        Task UpsertMapStat(RaceOnMapRatio raceOnMapRatio);
        Task<MapAndRaceRatio> LoadMapAndRaceStat(string battleTag);
        Task UpsertMapAndRaceStat(MapAndRaceRatio mapAndRaceRatio);
    }
}