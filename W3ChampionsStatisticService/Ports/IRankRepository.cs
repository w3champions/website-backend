using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.Ports
{
    public interface IRankRepository
    {
        Task<List<Rank>> LoadPlayersOfLeague(int leagueId, GateWay gateWay, GameMode gameMode);
        Task<List<Rank>> SearchPlayerOfLeague(string searchFor, GateWay gateWay, GameMode gameMode);
        Task<List<Rank>> LoadPlayerOfLeague(string searchFor);
        Task<List<LeagueConstellation>> LoadLeagueConstellation();
        Task InsertRanks(List<Rank> events);
        Task InsertLeagues(List<LeagueConstellation> leagueConstellations);
    }
}