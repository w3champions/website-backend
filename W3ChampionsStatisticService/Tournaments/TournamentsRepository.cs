using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.ReadModelBase;
using W3ChampionsStatisticService.Tournaments.Tournaments;

namespace W3ChampionsStatisticService.Tournaments
{
    public class TournamentsRepository : MongoDbRepositoryBase
    {
        public TournamentsRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public async Task<List<Tournament>> GetAll()
        {
            var result = await LoadAll<Tournament>();

            return result;
        }

        public Task Save(Tournament tournament)
        {
           return Upsert(tournament, p => p.Id == tournament.Id);
        }
    }
}
