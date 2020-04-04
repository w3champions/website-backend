using System.Threading.Tasks;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.W3ChampionsStats.RaceAndWinStats
{
    public class W3StatsRepo : MongoDbRepositoryBase, IW3StatsRepo
    {
        public W3StatsRepo(DbConnctionInfo connectionInfo) : base(connectionInfo)
        {
        }

        public Task<Wc3Stats> Load()
        {
            return LoadFirst<Wc3Stats>(s => s.Id == "W3Stats");
        }

        public Task Save(Wc3Stats stat)
        {
            return Upsert(stat, s => s.Id == stat.Id);
        }
    }
}