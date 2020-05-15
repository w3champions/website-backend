using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;

namespace W3ChampionsStatisticService.Ports
{
    public interface IRankRepository
    {
        Task<List<Rank>> LoadPlayersOfLeague(int leagueId, int season, GateWay gateWay, GameMode gameMode);
        Task<List<Rank>> SearchPlayerOfLeague(string searchFor, int season, GateWay gateWay, GameMode gameMode);
        Task<List<Rank>> LoadPlayerOfLeague(string searchFor);
        Task<List<LeagueConstellation>> LoadLeagueConstellation(int? season = null);
        Task InsertRanks(List<Rank> events);
        Task InsertLeagues(List<LeagueConstellation> leagueConstellations);
        Task UpsertSeason(Season season);
        Task<List<Season>> LoadSeasons();
    }
}