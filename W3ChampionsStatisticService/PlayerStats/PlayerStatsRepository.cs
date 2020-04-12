using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerStats
{
    public class PlayerStatsRepository : MongoDbRepositoryBase, IPlayerStatsRepository
    {
        public Task<RaceOnMapVersusRaceRatio> LoadMapAndRaceStat(string battleTag)
        {
            return LoadFirst<RaceOnMapVersusRaceRatio>(p => p.Id == battleTag);
        }

        public Task UpsertMapAndRaceStat(RaceOnMapVersusRaceRatio raceOnMapVersusRaceRatio)
        {
            return Upsert(raceOnMapVersusRaceRatio, p => p.Id == raceOnMapVersusRaceRatio.Id);
        }

        public PlayerStatsRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }
    }
}