using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Ports
{
    public interface IAdminRepository
    {
        Task Reset(string readModelType);
    }
}