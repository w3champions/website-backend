using System;
using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.W3ChampionsStats.HourOfPlay
{
    public class HourOfPlayStats
    {
        public void Apply(GameMode gameMode, DateTimeOffset timeOfGame, DateTimeOffset now = default)
        {
            now = now == default ? DateTimeOffset.UtcNow.Date : now.Date;

            var gameLengthPerMode = PlayTimesPerModeTwoWeeks.SingleOrDefault(m => m.GameMode == gameMode
                                                          && m.Day.Date == timeOfGame.Date);

            if (gameLengthPerMode == null)
            {
                PlayTimesPerModeTwoWeeks.Remove(PlayTimesPerModeTwoWeeks.Last());
                PlayTimesPerModeTwoWeeks.Remove(PlayTimesPerModeTwoWeeks.Last());
                PlayTimesPerModeTwoWeeks.Remove(PlayTimesPerModeTwoWeeks.Last());
                PlayTimesPerModeTwoWeeks.Remove(PlayTimesPerModeTwoWeeks.Last());

                PlayTimesPerModeTwoWeeks.Reverse();
                AddDay(PlayTimesPerModeTwoWeeks, GameMode.FFA, 0, now);
                AddDay(PlayTimesPerModeTwoWeeks, GameMode.GM_4v4, 0, now);
                AddDay(PlayTimesPerModeTwoWeeks, GameMode.GM_2v2, 0, now);
                AddDay(PlayTimesPerModeTwoWeeks, GameMode.GM_1v1, 0, now);
                PlayTimesPerModeTwoWeeks.Reverse();
            }

            gameLengthPerMode = PlayTimesPerModeTwoWeeks.Single(m => m.GameMode == gameMode
                                                          && m.Day.Date == timeOfGame.Date);

            gameLengthPerMode.Record(timeOfGame);
            PlayTimesPerMode = CalculateAverage(PlayTimesPerModeTwoWeeks);
        }

        private List<HourOfPlayPerMode> CalculateAverage(List<HourOfPlayPerMode> playTimesPerModeTwoWeeks)
        {
            var groupBy = playTimesPerModeTwoWeeks.GroupBy(f => f.GameMode);
            return groupBy.Select(f => CreateThing(f)).ToList();
        }

        private HourOfPlayPerMode CreateThing(IGrouping<GameMode,HourOfPlayPerMode> hourOfPlayPerModes)
        {
            var hourOfPlaysPerDay = hourOfPlayPerModes
                .SelectMany(h => h.PlayTimePerHour)
                .GroupBy(d => d.Time.TimeOfDay)
                .ToList();
            var hourOfPlays = hourOfPlaysPerDay.Select(f => new HourOfPlay
            {
                Time = new DateTimeOffset(new DateTime(2000, 1, 1, f.Key.Hours, f.Key.Minutes, 0)),
                Games = f.Sum(r => r.Games)
            }).ToList();
            return new HourOfPlayPerMode
            {
                PlayTimePerHour = hourOfPlays,
                GameMode = hourOfPlayPerModes.First().GameMode,
                Day = hourOfPlayPerModes.First().Day
            };
        }

        public List<HourOfPlayPerMode> PlayTimesPerModeTwoWeeks { get; set; } = new List<HourOfPlayPerMode>();
        public List<HourOfPlayPerMode> PlayTimesPerMode { get; set; } = new List<HourOfPlayPerMode>();

        public string Id { get; set; } = nameof(HourOfPlayStats);

        public static HourOfPlayStats Create(DateTimeOffset time = default)
        {
            time = time == default ? DateTime.UtcNow.Date : time;

            var average = new List<HourOfPlayPerMode>();
            AddDay(average, GameMode.GM_1v1, 0, time);
            AddDay(average, GameMode.GM_2v2, 0, time);
            AddDay(average, GameMode.GM_4v4, 0, time);
            AddDay(average, GameMode.FFA, 0, time);

            return new HourOfPlayStats
            {
                PlayTimesPerModeTwoWeeks = Create14DaysOfPlaytime(time),
                PlayTimesPerMode = average
            };
        }

        private static List<HourOfPlayPerMode> Create14DaysOfPlaytime(DateTimeOffset time)
        {
            var hours = new List<HourOfPlayPerMode>();
            for (int i = 0; i < 14; i++)
            {
                AddDay(hours, GameMode.GM_1v1, i, time);
                AddDay(hours, GameMode.GM_2v2, i, time);
                AddDay(hours, GameMode.GM_4v4, i, time);
                AddDay(hours, GameMode.FFA, i, time);
            }

            return hours;
        }

        private static void AddDay(List<HourOfPlayPerMode> hours, GameMode gameMode, int i, DateTimeOffset time)
        {
            hours.Add(new HourOfPlayPerMode
            {
                GameMode = gameMode,
                PlayTimePerHour = CreateLengths(time.AddDays(-i)),
                Day = time.AddDays(-i)
            });
        }

        private static List<HourOfPlay> CreateLengths(DateTimeOffset day)
        {
            var lengths = new List<HourOfPlay>();
            for (var i = 0; i < 96; i++) // every 15 minutes
            {
                lengths.Add(new HourOfPlay { Time = day.AddMinutes(i * 15)});
            }

            return lengths;
        }
    }
}