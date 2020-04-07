using System.Linq;

namespace W3ChampionsStatisticService.Matches
{
    public class MapName
    {
        public MapName(string matchMap)
        {
            Name = matchMap.Split("/").Last().Replace(".w3x", "").Replace("_lv", "").Substring(3);
        }

        public string Name { get; }
    }
}