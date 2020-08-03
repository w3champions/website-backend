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
                .Replace("_", "");
        }

        public string Name { get; }
    }
}