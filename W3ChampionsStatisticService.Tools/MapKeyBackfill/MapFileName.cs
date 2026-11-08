using System.Linq;

namespace W3ChampionsStatisticService.Tools.MapKeyBackfill;

public static class MapFileName
{
    // Same as the service's MapName: the file name of a map path. A file name can't contain '/' or '\'.
    public static string Of(string path)
    {
        return path?.Split('/', '\\').Last();
    }
}
