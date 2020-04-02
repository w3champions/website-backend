namespace W3ChampionsStatisticService.Matches
{
    public class MapName
    {
        public MapName(string matchMap)
        {
            Name = matchMap.Split("/")[3].Replace(".w3x", "").Substring(3);
        }

        public string Name { get; }
    }
}