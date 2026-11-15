using System.Linq;

namespace W3ChampionsStatisticService.Tools.MapKeyBackfill;

public static class LegacyMapKey
{
    // Verbatim copy of the old W3ChampionsStatisticService/Matches/MapName.cs mangling, which produced the
    // keys stored in the read models before the switch to file names. Keep the order of the calls as is.
    // Additions: null for a null path, and null where the old code threw in Substring(3), since no key
    // was ever stored for such paths.
    public static string Compute(string path)
    {
        if (path == null)
        {
            return null;
        }

        var name = path.Split('/', '\\')
            .Last()
            .Replace(".w3x", "")
            .Replace(".w3m", "")
            .Replace("_lv", "");

        if (name.Length < 3)
        {
            return null;
        }

        return name
            .Substring(3)
            .Replace("_lv_anon", "")
            .Replace("_anon", "")
            .Replace(".anon", "")
            .Replace("ffa_", "")
            .Replace("1v1_", "")
            .Replace("2v2_", "")
            .Replace("4v4_", "")
            .Replace("mur'galoasis", "murguloasis")
            .Replace("mur'guloasis", "murguloasis")
            .Replace("battlegrounds", "battleground")
            .Replace("goleminthemist", "golemsinthemist")
            .Replace("_cd", "")
            .Replace("_", "")
            .Replace(".", "_");
    }
}
