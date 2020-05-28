using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.PlayerStats.HeroStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerStats
{
    public class PlayerStatsRepository : MongoDbRepositoryBase, IPlayerStatsRepository
    {
        public Task<PlayerRaceOnMapVersusRaceRatio> LoadMapAndRaceStat(string battleTag, int season)
        {
            return LoadFirst<PlayerRaceOnMapVersusRaceRatio>(p => p.Id == $"{season}_{battleTag}");
        }

        public Task<PlayerHeroStats> LoadHeroStat(string battleTag, int season)
        {
            return LoadFirst<PlayerHeroStats>(p => p.Id == $"{season}_{battleTag}");
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