using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerStats.HeroStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace W3ChampionsStatisticService.Ports;

public interface IPlayerStatsRepository
{
    Task<PlayerRaceOnMapVersusRaceRatio> LoadMapAndRaceStat(string battleTag, int season);
    Task<PlayerHeroStats> LoadHeroStat(string battleTag, int season);
    Task UpsertMapAndRaceStat(PlayerRaceOnMapVersusRaceRatio playerRaceOnMapVersusRaceRatio);
    Task UpsertPlayerHeroStats(PlayerHeroStats playerHeroStats);
    int LoadMaxMMR();
}
