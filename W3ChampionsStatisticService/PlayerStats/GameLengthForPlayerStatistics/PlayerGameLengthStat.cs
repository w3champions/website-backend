using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.W3ChampionsStats.GameLengths;

namespace W3ChampionsStatisticService.PlayerStats.GameLengthForPlayerStatistics;

public class PlayerGameLengthStat
{
    // the dictionary key is the interval start: 0, 30, 60, 90, 120..
    // the dictionary value is the number of games
    public Dictionary<string, int> Lengths { get; set; }

    public void Record(int duration)
    {
        var groupInterval = 30;
        var maxGroupValue = 120;
        var group = (int)duration / groupInterval;
        group = group > maxGroupValue ? maxGroupValue : group;
        group*=30;
        var groupString = group.ToString();
        if (!Lengths.ContainsKey(groupString)) {
            Lengths.Add(groupString, 0);
        }
        Lengths[groupString]++;
    }

    public void Apply(int duration)
    {
        Record(duration);
    }

    public static PlayerGameLengthStat Create()
    {
        return new PlayerGameLengthStat
        {
            Lengths = new Dictionary<string, int>()
        };
    }
}
