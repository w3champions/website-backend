using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace W3ChampionsStatisticService.Ports
{
    public interface IPlayerStatisticsService
    {
        Task<PlayerRaceOnMapVersusRaceRatioView> GetMapAndRaceStatAsync(string battleTag, int season);
    }
}