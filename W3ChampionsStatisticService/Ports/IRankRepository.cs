using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PlayerProfiles;

namespace W3ChampionsStatisticService.Ports
{
    public interface IRankRepository
    {
        Task<List<Rank>> LoadPlayersOfLeague(int leagueId, int season, GateWay gateWay, GameMode gameMode);
        Task<List<Rank>> SearchPlayerOfLeague(string searchFor, int season, GateWay gateWay, GameMode gameMode);
        Task<List<Rank>> LoadPlayerOfLeague(string searchFor, int season);
        Task<List<LeagueConstellation>> LoadLeagueConstellation(int season);
        Task InsertRanks(List<Rank> events);
        Task InsertLeagues(List<LeagueConstellation> leagueConstellations);
        Task UpsertSeason(Season season);
        Task<List<Season>> LoadSeasons();
    }
}