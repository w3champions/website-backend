using System.Collections.Generic;
using System.Threading.Tasks;

namespace W3ChampionsStatisticService.PadEvents.FakeEventSync
{
    public interface ITempLossesRepo
    {
        Task<List<RaceAndWinDto>> LoadLosses(string playerAccount);
        Task<List<RaceAndWinDto>> LoadWins(string playerAccount);
        Task SaveWins(string playerAccount, List<RaceAndWinDto> newDiffWins);
        Task SaveLosses(string playerAccount, List<RaceAndWinDto> newDiffLosses);
    }
}