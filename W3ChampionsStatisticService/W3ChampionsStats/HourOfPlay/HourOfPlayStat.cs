using System;
using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;

// TODO rename file to PopularHoursStat

namespace W3ChampionsStatisticService.W3ChampionsStats.HourOfPlay
{
    public class HourOfPlayStat2 : IIdentifiable
    {
        public List<HourOfPlayPerMode> PlayTimesPerModeTwoWeeks { get; set; } = new List<HourOfPlayPerMode>();
        public HourOfPlayPerMode PlayTimesPerModeTotal { get; set; } = new HourOfPlayPerMode();
        public string Id => GameMode.ToString();
        public GameMode GameMode { get; set; }

        public void Apply(GameMode gameMode, DateTime gameStartTime)
        {
            var now = DateTime.UtcNow.Date;
            var daysOfDifference = now - gameStartTime.Date;
            if (daysOfDifference >= TimeSpan.FromDays(14))
            {
                return;
            }

            var gameLengthPerMode = PlayTimesPerModeTwoWeeks.SingleOrDefault(m => m.Day.Date == gameStartTime.Date);

            // No stats found for the the day in question.
            // This means the date has shifted, so we need to remove stats for the oldest date and add a new day.
            if (gameLengthPerMode == null)
            {
                PlayTimesPerModeTwoWeeks.Remove(PlayTimesPerModeTwoWeeks.First());
                AddDay(PlayTimesPerModeTwoWeeks, gameMode, 0, now);

                gameLengthPerMode = PlayTimesPerModeTwoWeeks.SingleOrDefault(m => m.Day.Date == gameStartTime.Date);
            }

            if (gameLengthPerMode != null) { 
                gameLengthPerMode.Record(gameStartTime);
            }
            PlayTimesPerModeTotal = CalculateTotal(PlayTimesPerModeTwoWeeks);
        }

        private HourOfPlayPerMode CalculateTotal(List<HourOfPlayPerMode> playTimesPerModeTwoWeeks)
        {
            var hourOfPlaysPerDay = playTimesPerModeTwoWeeks
                .SelectMany(h => h.PlayTimePerHour)
                .GroupBy(d => d.Time.TimeOfDay)
                .ToList();
            var hourOfPlays = hourOfPlaysPerDay.Select(f => new HourOfPlay
            {
                Time = new DateTime(2000, 1, 1, f.Key.Hours, f.Key.Minutes, 0, DateTimeKind.Utc),
                Games = f.Sum(r => r.Games)
            }).ToList();
            return new HourOfPlayPerMode
            {
                PlayTimePerHour = hourOfPlays,
                Day = playTimesPerModeTwoWeeks.Last().Day
            };
        }

        public static HourOfPlayStat2 Create(GameMode mode)
        {
            var today = DateTime.UtcNow.Date;
            var hours = new HourOfPlayPerMode
            {
                PlayTimePerHour = CreateTimeslots(today.AddDays(0)),
                Day = today.AddDays(0)
            };

            return new HourOfPlayStat2
            {
                GameMode = mode,
                PlayTimesPerModeTwoWeeks = Create14Days(today, mode),
                PlayTimesPerModeTotal = hours
            };
        }

        private static List<HourOfPlayPerMode> Create14Days(DateTime day, GameMode mode)
        {
            var hours = new List<HourOfPlayPerMode>();

            // Start adding days from 2 weeks ago, going forward, until today's date.
            for (int i = 13; i >= 0; i--)
            {
                AddDay(hours, mode, i, day);
            }

            return hours;
        }

        // TODO rename type to GamesPerTimeslotPerDay
        private static void AddDay(List<HourOfPlayPerMode> twoWeekStats, GameMode gameMode, int i, DateTime day)
        {
            twoWeekStats.Add(new HourOfPlayPerMode
            {
                PlayTimePerHour = CreateTimeslots(day.AddDays(-i)),
                Day = day.AddDays(-i)
            });
        }

        // TODO rename type to GamesPerTimeslot
        private static List<HourOfPlay> CreateTimeslots(DateTime day)
        {
            var timeslots = new List<HourOfPlay>();
            for (var i = 0; i < 96; i++) // 15 minute intervals
            {
                timeslots.Add(new HourOfPlay { Time = day.AddMinutes(i * 15) });
            }

            return timeslots;
        }
    }
}
