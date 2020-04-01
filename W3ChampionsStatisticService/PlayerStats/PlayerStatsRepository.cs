using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.PlayerStats.RaceVersusRaceStats;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.PlayerStats
{
    public class PlayerStatsRepository : MongoDbRepositoryBase, IPlayerStatsRepository
    {
        public Task<PlayerRaceLossRatio> LoadRaceStat(string battleTag)
        {
            return LoadFirst<PlayerRaceLossRatio>(p => p.Id == battleTag);
        }

        public Task UpsertRaceStat(PlayerRaceLossRatio playerRaceLossRatio)
        {
            return Upsert(playerRaceLossRatio, p => p.Id == playerRaceLossRatio.Id);
        }

        public Task<RaceOnMapRatio> LoadMapStat(string battleTag)
        {
            return LoadFirst<RaceOnMapRatio>(p => p.Id == battleTag);
        }

        public Task UpsertMapStat(RaceOnMapRatio raceOnMapRatio)
        {
            return Upsert(raceOnMapRatio, p => p.Id == raceOnMapRatio.Id);
        }

        public Task<MapAndRaceRatio> LoadMapAndRaceStat(string battleTag)
        {
            return LoadFirst<MapAndRaceRatio>(p => p.Id == battleTag);
        }

        public Task UpsertMapAndRaceStat(MapAndRaceRatio mapAndRaceRatio)
        {
            return Upsert(mapAndRaceRatio, p => p.Id == mapAndRaceRatio.Id);
        }

        public PlayerStatsRepository(DbConnctionInfo connectionInfo) : base(connectionInfo)
        {
        }
    }
}