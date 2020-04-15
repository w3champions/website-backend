using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.W3ChampionsStats.HourOfPlay
{
    public class HourOfPlayStats
    {
        public void Apply(GameMode gameMode, DateTimeOffset day, DateTime time = default)
        {
            time = time == default ? DateTime.UtcNow.Date : time;

            var gameLengthPerMode = PlayTimesPerMode.SingleOrDefault(m => m.GameMode == gameMode
                                                          && m.Day == day.Date);

            if (gameLengthPerMode == null)
            {
                PlayTimesPerMode.Remove(PlayTimesPerMode[0]);
                PlayTimesPerMode.Remove(PlayTimesPerMode[1]);
                PlayTimesPerMode.Remove(PlayTimesPerMode[2]);
                PlayTimesPerMode.Remove(PlayTimesPerMode[3]);

                AddDay(PlayTimesPerMode, GameMode.GM_1v1, 0, time);
                AddDay(PlayTimesPerMode, GameMode.GM_2v2, 0, time);
                AddDay(PlayTimesPerMode, GameMode.GM_4v4, 0, time);
                AddDay(PlayTimesPerMode, GameMode.FFA, 0, time);
            }

            gameLengthPerMode = PlayTimesPerMode.Single(m => m.GameMode == gameMode
                                                          && m.Day == day.Date);

            gameLengthPerMode.Record(day);
        }

        [JsonIgnore]
        public List<HourOfPlayPerMode> PlayTimesPerMode { get; set; } = new List<HourOfPlayPerMode>();

        public string Id { get; set; } = nameof(HourOfPlayStats);

        public static HourOfPlayStats Create(DateTime time = default)
        {
            time = time == default ? DateTime.UtcNow.Date : time;
            return new HourOfPlayStats
            {
                PlayTimesPerMode = Create14DaysOfPlaytime(time)
            };
        }

        private static List<HourOfPlayPerMode> Create14DaysOfPlaytime(DateTime time)
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

        private static void AddDay(List<HourOfPlayPerMode> hours, GameMode gameMode, int i, DateTime time)
        {
            hours.Add(new HourOfPlayPerMode
            {
                GameMode = gameMode,
                PlayTimePerHour = CreateLengths(),
                Day = time.AddDays(-i)
            });
        }

        private static List<HourOfPlay> CreateLengths()
        {
            var lengths = new List<HourOfPlay>();
            var now = DateTimeOffset.UtcNow.Date;
            for (var i = 0; i < 96; i++) // every 15 minutes
            {
                lengths.Add(new HourOfPlay { Time = now.AddMinutes(i * 15)});
            }

            return lengths;
        }
    }
}