using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerStats.HeroStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace W3ChampionsStatisticService.Ports
{
    public interface IPlayerStatsRepository
    {
        Task<RaceOnMapVersusRaceRatio> LoadMapAndRaceStat(string battleTag, int season);
        Task<PlayerHeroStats> LoadHeroStat(string battleTag, int season);
        Task UpsertMapAndRaceStat(RaceOnMapVersusRaceRatio raceOnMapVersusRaceRatio);
        Task UpsertPlayerHeroStats(PlayerHeroStats playerHeroStats);
    }
}