using System;
using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.W3ChampionsStats.PopularHours;

public class PopularHoursStat : IIdentifiable
{
    public string Id => GameMode.ToString();
    public GameMode GameMode { get; set; }
    public List<DayOfTimeslots> PopularHoursTwoWeeks { get; set; } = new List<DayOfTimeslots>();
    public DayOfTimeslots PopularHoursTotal { get; set; } = new DayOfTimeslots();

    public void Apply(DateTime gameStartTime)
    {
        var now = DateTime.UtcNow.Date;
        var daysOfDifference = now - gameStartTime.Date;
        if (daysOfDifference >= TimeSpan.FromDays(14))
        {
            return;
        }

        var popularHoursStat = PopularHoursTwoWeeks.SingleOrDefault(m => m.Day.Date == gameStartTime.Date);

        // No stats found for the the day in question.
        // This means the date has shifted, so we need to remove stats for the oldest date and add a new day.
        if (popularHoursStat == null)
        {
            PopularHoursTwoWeeks.Remove(PopularHoursTwoWeeks.First());
            AddDay(PopularHoursTwoWeeks, 0, gameStartTime.Date);

            popularHoursStat = PopularHoursTwoWeeks.SingleOrDefault(m => m.Day.Date == gameStartTime.Date);
        }

        popularHoursStat.Record(gameStartTime);

        PopularHoursTotal = CalculateTotal(PopularHoursTwoWeeks);
    }

    private DayOfTimeslots CalculateTotal(List<DayOfTimeslots> PopularHoursTwoWeeks)
    {
        var groupedByTimeslot = PopularHoursTwoWeeks
            .SelectMany(h => h.Timeslots)
            .GroupBy(d => d.Time.TimeOfDay)
            .ToList();
        var summedTimeslots = groupedByTimeslot.Select(f => new Timeslot
        {
            Time = new DateTime(2000, 1, 1, f.Key.Hours, f.Key.Minutes, 0, DateTimeKind.Utc),
            Games = f.Sum(r => r.Games)
        }).ToList();
        return new DayOfTimeslots
        {
            Timeslots = summedTimeslots,
            Day = PopularHoursTwoWeeks.Last().Day
        };
    }

    public static PopularHoursStat Create(GameMode mode)
    {
        var today = DateTime.UtcNow.Date;
        var hours = new DayOfTimeslots
        {
            Timeslots = CreateTimeslots(today.AddDays(0)),
            Day = today.AddDays(0)
        };

        return new PopularHoursStat
        {
            GameMode = mode,
            PopularHoursTwoWeeks = Create14Days(today),
            PopularHoursTotal = hours
        };
    }

    private static List<DayOfTimeslots> Create14Days(DateTime day)
    {
        var hours = new List<DayOfTimeslots>();

        // Start adding days from 2 weeks ago, going forward, until today's date.
        for (int i = 13; i >= 0; i--)
        {
            AddDay(hours, i, day);
        }

        return hours;
    }

    private static void AddDay(List<DayOfTimeslots> twoWeekStats, int i, DateTime day)
    {
        twoWeekStats.Add(new DayOfTimeslots
        {
            Timeslots = CreateTimeslots(day.AddDays(-i)),
            Day = day.AddDays(-i)
        });
    }

    private static List<Timeslot> CreateTimeslots(DateTime day)
    {
        var timeslots = new List<Timeslot>();
        for (var i = 0; i < 96; i++) // 15 minute intervals
        {
            timeslots.Add(new Timeslot { Time = day.AddMinutes(i * 15) });
        }

        return timeslots;
    }
}
