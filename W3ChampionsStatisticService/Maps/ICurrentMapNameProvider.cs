using System.Threading.Tasks;

namespace W3ChampionsStatisticService.Maps;

public interface ICurrentMapNameProvider
{
    /// <summary>Never throws: if matchmaking can't be reached, returns <see cref="CurrentMapNames.Empty"/>, so stored names are shown.</summary>
    Task<CurrentMapNames> GetAsync();
}
