using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerStats.HeroStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace W3ChampionsStatisticService.Ports
{
    public interface IPlayerStatsRepository
    {
        Task<RaceOnMapVersusRaceRatio> LoadMapAndRaceStat(string battleTag);
        Task<PlayerHeroStats> LoadHeroStat(string battleTag);
        Task UpsertMapAndRaceStat(RaceOnMapVersusRaceRatio raceOnMapVersusRaceRatio);
        Task UpsertPlayerHeroStats(PlayerHeroStats playerHeroStats);
    }
}