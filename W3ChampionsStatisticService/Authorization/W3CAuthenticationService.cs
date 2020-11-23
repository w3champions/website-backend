using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.ReadModelBase;

namespace W3ChampionsStatisticService.Authorization
{
    public class W3CAuthenticationService : MongoDbRepositoryBase, IW3CAuthenticationService
    {
        public Task Save(W3CUserAuthentication user)
        {
            return Upsert(user);
        }

        public Task<W3CUserAuthentication> GetUser(string bearer)
        {
            return LoadFirst<W3CUserAuthentication>(u => u.Token == bearer);

        }

        public W3CAuthenticationService(MongoClient mongoClient) : base(mongoClient)
        {
        }
    }

    public interface IW3CAuthenticationService
    {
        Task Save(W3CUserAuthentication user);
        Task<W3CUserAuthentication> GetUser(string bearer);
    }
}