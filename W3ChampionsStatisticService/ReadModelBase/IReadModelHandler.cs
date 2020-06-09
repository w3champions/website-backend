using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.ReadModelBase
{
    public interface IReadModelHandler
    {
        Task Update(MatchFinishedEvent nextEvent);
        void ResetReadModelDbName()
        {
            var fieldInfos = GetType().GetFields();

            var enumerable = fieldInfos.Where(f => f.FieldType.IsAssignableFrom(typeof(MongoDbRepositoryBase)));
            foreach (var fieldInfo in enumerable)
            {
                var repository = fieldInfo.GetValue(this) as MongoDbRepositoryBase;
                repository?.SetAsTempRepo();
            }
        }
    }
}