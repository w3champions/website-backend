using System.Collections.Generic;
using System.Linq;
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

        public Task<List<PortraitDefinition>> LoadPortraitDefinitions()
        {
            return LoadAll<PortraitDefinition>();
        }

        public async Task SaveNewPortraitDefinitions(List<int> portraitIds)
        {
            var existingPortraits = await LoadPortraitDefinitions();
            var toAdd = portraitIds.Distinct().ToList();
            foreach (var id in toAdd)
            {
                if (!existingPortraits.Any(x => x.Id == id))
                {
                    await Insert(new PortraitDefinition(id));
                }
            }
        }

        public async Task DeletePortraitDefinitions(List<int> portraitIds)
        {
            var existingPortraits = await LoadPortraitDefinitions();
            var toDelete = portraitIds.Distinct().ToList();
            foreach (var id in toDelete)
            {
                if (existingPortraits.Any(x => x.Id == id))
                {
                    await Delete<PortraitDefinition>(n => n.Id == id);
                }
            }
        }
    }
}