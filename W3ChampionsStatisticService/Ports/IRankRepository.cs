using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.Ports
{
    public interface IRankRepository
    {
        Task<List<Rank>> LoadPlayerOfLeague(int leagueId, int gateWay);
        Task<List<Rank>> LoadPlayerOfLeagueLike(string searchFor, int gateWay);
        Task<List<LeagueConstellationChangedEvent>> LoadLeagueConstellation();
    }
}