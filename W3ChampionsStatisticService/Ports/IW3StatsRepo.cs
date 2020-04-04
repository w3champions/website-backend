using System.Threading.Tasks;
using W3ChampionsStatisticService.W3ChampionsStats;
using W3ChampionsStatisticService.W3ChampionsStats.GamesPerDay;
using W3ChampionsStatisticService.W3ChampionsStats.RaceAndWinStats;

namespace W3ChampionsStatisticService.Ports
{
    public interface IW3StatsRepo
    {
        Task<Wc3Stats> Load();
        Task Save(Wc3Stats stat);
        Task<GamesPerDay> LoadGamesPerDay();
        Task Save(GamesPerDay stat);
    }
}