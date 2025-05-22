using MongoDB.Driver;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.PlayerStats.HeroStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.PlayerStats;

[Trace]
public class PlayerStatsRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IPlayerStatsRepository
{
    public Task<PlayerRaceOnMapVersusRaceRatio> LoadMapAndRaceStat(string battleTag, int season)
    {
        return LoadFirst<PlayerRaceOnMapVersusRaceRatio>($"{season}_{battleTag}");
    }

    public Task<PlayerHeroStats> LoadHeroStat(string battleTag, int season)
    {
        return LoadFirst<PlayerHeroStats>($"{season}_{battleTag}");
    }

    public Task UpsertMapAndRaceStat(PlayerRaceOnMapVersusRaceRatio playerRaceOnMapVersusRaceRatio)
    {
        return Upsert(playerRaceOnMapVersusRaceRatio);
    }

    public Task UpsertPlayerHeroStats(PlayerHeroStats playerHeroStats)
    {
        return Upsert(playerHeroStats);
    }
}
