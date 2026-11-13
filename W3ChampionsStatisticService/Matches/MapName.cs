using System.Linq;

namespace W3ChampionsStatisticService.Matches;

public class MapName(string map)
{
    public string Name { get; } = map?.Split('/', '\\').Last();
}
