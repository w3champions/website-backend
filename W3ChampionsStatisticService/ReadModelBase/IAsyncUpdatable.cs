using System.Threading.Tasks;

namespace W3ChampionsStatisticService.ReadModelBase
{
    public interface IAsyncUpdatable
    {
        Task Update();
    }
}