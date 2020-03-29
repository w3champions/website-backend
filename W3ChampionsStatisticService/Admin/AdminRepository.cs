using System.Threading.Tasks;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Admin
{
    public class AdminRepository : MongoDbRepositoryBase, IAdminRepository
    {
        public AdminRepository(DbConnctionInfo connctionInfo) : base(connctionInfo)
        {
        }

        public async Task Reset(string readModelType)
        {
            var mongoDatabase = CreateClient();
            await mongoDatabase.DropCollectionAsync(readModelType);
        }
    }
}