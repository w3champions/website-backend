using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using W3ChampionsStatisticService.Admin.Portraits;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Admin
{
    public class PortraitRepository : MongoDbRepositoryBase, IPortraitRepository
    {
        public PortraitRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public Task<PortraitDefinitions> GetPortraits()
        {
            var mongoCollection = CreateCollection<PortraitDefinitions>();
            return mongoCollection
                .Find(r => true)
                .SortBy(x => x.Ids)
                .SingleAsync();
            // TODO - resolve collection type
        }

        public async Task SaveNewPortraits(List<int> portraitIds)
        {
            var existingPortraits = await GetPortraits();
            foreach (var id in portraitIds)
            {
                if (!existingPortraits.Ids.Contains(id))
                {
                    await Insert(id);
                }
            }
        }

        public async Task DeletePortraits(List<int> portraitIds)
        {
            var existingPortraits = await GetPortraits();
            foreach (var id in portraitIds)
            {
                if (existingPortraits.Ids.Contains(id))
                {
                    await Delete<PortraitDefinitions>(n => n.Ids == id);
                }
            }
        }
    }
}