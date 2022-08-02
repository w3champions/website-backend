using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using MongoDB.Bson;
using W3C.Domain.CommonValueObjects;

namespace W3C.Domain.Repositories
{
    public interface IInformationMessagesRepository
    {
        Task<List<LoadingScreenTip>> GetTips(int? limit = 5);
        Task<LoadingScreenTip> GetRandomTip();
        Task Save(LoadingScreenTip loadingScreenTip);
        Task DeleteTip(ObjectId objectId);
        Task UpsertTip(LoadingScreenTip loadingScreenTip);
        Task<MessageOfTheDay> GetMotd();
        Task<HttpStatusCode> SetMotd(MessageOfTheDay motd);
    }
}
