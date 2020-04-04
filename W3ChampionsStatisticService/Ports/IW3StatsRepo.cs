using System.Threading.Tasks;
using W3ChampionsStatisticService.W3ChampionsStats.GameLengths;
using W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.RaceAndWinStats;

namespace W3ChampionsStatisticService.Ports
{
    public interface IW3StatsRepo
    {
        Task<Wc3Stats> Load();
        Task Save(Wc3Stats stat);
        Task<GamesPerDay> LoadGamesPerDay();
        Task Save(GamesPerDay stat);
        Task<GameLengthStats> LoadGameLengths();
        Task Save(GameLengthStats stat);
    }
}