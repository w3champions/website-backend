using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.PlayerStats.RaceVersusRaceStats;

namespace W3ChampionsStatisticService.Ports
{
    public interface IPlayerStatsRepository
    {
        Task<RaceVersusRaceRatio> LoadRaceStat(string battleTag);
        Task UpsertRaceStat(RaceVersusRaceRatio raceVersusRaceRatio);
        Task<RaceOnMapRatio> LoadMapStat(string battleTag);
        Task UpsertMapStat(RaceOnMapRatio raceOnMapRatio);
        Task<RaceOnMapVersusRaceRatio> LoadMapAndRaceStat(string battleTag);
        Task UpsertMapAndRaceStat(RaceOnMapVersusRaceRatio raceOnMapVersusRaceRatio);
    }
}