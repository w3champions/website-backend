using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace W3ChampionsStatisticService.Ports
{
    public interface IPlayerStatsRepository
    {
        Task<RaceOnMapVersusRaceRatio> LoadMapAndRaceStat(string battleTag);
        Task UpsertMapAndRaceStat(RaceOnMapVersusRaceRatio raceOnMapVersusRaceRatio);
    }
}