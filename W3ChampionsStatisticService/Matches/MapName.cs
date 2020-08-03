using System.Linq;

namespace W3ChampionsStatisticService.Matches
{
    public class MapName
    {
        public MapName(string map)
        {
            Name = map.Split("/")
                .Last()
                .Replace(".w3x", "")
                .Replace("_lv", "")
                .Substring(3)
                .Replace("_lv_anon", "")
                .Replace("_anon", "")
                .Replace("ffa_", "")
                .Replace("1v1_", "")
                .Replace("2v2_", "")
                .Replace("4v4_", "")
                .Replace("mur'galoasis", "murguloasis")
                .Replace("mur'guloasis", "murguloasis")
                .Replace("battlegrounds", "battleground")
                .Replace("goleminthemist", "golemsinthemist")
                .Replace("_cd", "")
                .Replace("_", "");
        }

        public string Name { get; }
    }
}