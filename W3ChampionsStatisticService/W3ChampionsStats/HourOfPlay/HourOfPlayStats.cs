using System;
using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.W3ChampionsStats.HourOfPlay
{
    public class HourOfPlayStats
    {public void Apply(GameMode gameMode, DateTimeOffset duration)
        {
            var gameLengthPerMode = GameLengths.Single(m => m.GameMode == gameMode);
            gameLengthPerMode.Record(duration);
        }

        public List<HourOfPlayPerMode> GameLengths { get; set; } = new List<HourOfPlayPerMode>();
        public string Id { get; set; } = nameof(HourOfPlayStats);

        public static HourOfPlayStats Create()
        {
            return new HourOfPlayStats
            {
                GameLengths = new List<HourOfPlayPerMode>
                {
                    new HourOfPlayPerMode
                    {
                        GameMode = GameMode.GM_1v1,
                        PlayTime = CreateLengths()
                    },
                    new HourOfPlayPerMode
                    {
                        GameMode = GameMode.GM_2v2,
                        PlayTime = CreateLengths()
                    },
                    new HourOfPlayPerMode
                    {
                        GameMode = GameMode.GM_4v4,
                        PlayTime = CreateLengths()
                    },
                    new HourOfPlayPerMode
                    {
                        GameMode = GameMode.FFA,
                        PlayTime = CreateLengths()
                    }
                }
            };
        }

        private static List<HourOfPlay> CreateLengths()
        {
            var lengths = new List<HourOfPlay>();
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i <= 96; i++) // every 15 minutes
            {
                lengths.Add(new HourOfPlay { Time = now.AddMinutes(i * 15)});
            }

            return lengths;
        }
    }
}