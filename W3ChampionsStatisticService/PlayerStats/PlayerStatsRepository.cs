using MongoDB.Driver;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.PlayerStats.HeroStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PlayerStats
{
    public class PlayerStatsRepository : MongoDbRepositoryBase, IPlayerStatsRepository
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

        public PlayerStatsRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }
    }
}