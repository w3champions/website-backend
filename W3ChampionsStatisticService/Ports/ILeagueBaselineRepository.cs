using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ladder;

namespace W3ChampionsStatisticService.Ports;

public interface ILeagueBaselineRepository
{
    Task<List<LeagueBaseline>> LoadByIds(List<string> ids);
    Task UpsertMany(List<LeagueBaseline> baselines);
}
