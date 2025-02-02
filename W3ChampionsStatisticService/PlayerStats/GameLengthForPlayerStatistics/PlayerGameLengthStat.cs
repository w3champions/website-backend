using System.Collections.Generic;

namespace W3ChampionsStatisticService.PlayerStats.GameLengthForPlayerStatistics;

public class PlayerGameLengthStat
{
    // the dictionary key is the interval start: 0, 60, 120.. (according to the groupInterval definition)
    // the dictionary value is the number of games
    public Dictionary<string, int> Lengths { get; set; }

    public void Record(int duration)
    {
        var groupInterval = 60;
        var maxGroupValue = 60;
        var group = (int)duration / groupInterval;
        group = group > maxGroupValue ? maxGroupValue : group;
        group *= groupInterval;
        var groupString = group.ToString();
        if (!Lengths.ContainsKey(groupString))
        {
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
