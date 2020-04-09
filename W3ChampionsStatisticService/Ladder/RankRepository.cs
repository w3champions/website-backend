using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Ladder
{
    public class RankRepository : MongoDbRepositoryBase, IRankeRepository
    {
        public RankRepository(DbConnctionInfo connectionInfo) : base(connectionInfo)
        {
        }

        public async Task<List<RankWithProfile>> LoadPlayerOfLeague(int leagueId, int gateWay)
        {
            var ranks = CreateCollection<Rank>();
            var players = CreateCollection<PlayerOverview>();
            var result = await ranks
                .Aggregate()
                .Match(rank => rank.League == leagueId && rank.Gateway == gateWay)
                .SortBy(rank => rank.RankNumber)
                .Lookup<Rank, PlayerOverview, RankWithProfile>(players,
                    rank => rank.PlayerId,
                    player => player.Id,
                    rank => rank.Players)
                .ToListAsync();
            return result.Where(r => r.Player != null).ToList();
        }

        public async Task Insert(List<Rank> events)
        {
            foreach (var ev in events)
            {
                await Upsert(ev, r => r.Id == ev.Id);
            }
        }
    }

    public class RankWithProfile
    {
        public int Gateway { get; set; }
        public string Id { get; set; }
        public int League { get; set; }
        public int RankNumber { get; set; }
        public double RankingPoints { get; }
        public string PlayerId { get; set; }
        [JsonIgnore]
        public List<PlayerOverview> Players { get; set; }
        public PlayerOverview Player => Players.SingleOrDefault();
    }
}