using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.Ports
{
    public interface IRankRepository
    {
        Task<List<Rank>> LoadPlayersOfLeague(int leagueId, int gateWay, GameMode gameMode);
        Task<List<Rank>> SearchPlayerOfLeague(string searchFor, int gateWay, GameMode gameMode);
        Task<Rank> LoadPlayerOfLeague(string searchFor);
        Task<List<LeagueConstellationChangedEvent>> LoadLeagueConstellation();
        Task InsertMany(List<Rank> events);
    }
}