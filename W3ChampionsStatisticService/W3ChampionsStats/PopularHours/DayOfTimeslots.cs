using System;
using System.Collections.Generic;
using System.Linq;

namespace W3ChampionsStatisticService.W3ChampionsStats.PopularHours;

public class DayOfTimeslots
{
    public List<Timeslot> Timeslots { get; set; }
    public DateTime Day { get; set; }

    public void Record(DateTime time)
    {
        var timeslots = Timeslots.Where(m => m.Time <= time);
        var ordered = timeslots.OrderBy(m => m.Time);
        var timeslot = ordered.Last();
        timeslot.AddGame();
    }
}
