using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Tournaments.Models;

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
