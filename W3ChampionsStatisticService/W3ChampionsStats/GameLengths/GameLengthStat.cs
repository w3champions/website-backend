using System;
using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.W3ChampionsStats.GameLengths
{
    public class GameLengthStat : IIdentifiable
    {
        public void Apply(GameMode gameMode, TimeSpan duration)
        {
            var gameLengthPerMode = GameLengths.SingleOrDefault(m => m.GameMode == gameMode);

            if (gameLengthPerMode != null)
            {
                gameLengthPerMode.Record(duration);
            }
        }

        public List<GameLengthPerMode> GameLengths { get; set; } = new List<GameLengthPerMode>();
        public string Id { get; set; } = nameof(GameLengthStat);

        public static GameLengthStat Create()
        {
            return new GameLengthStat
            {
                GameLengths = new List<GameLengthPerMode>
                {
                    new GameLengthPerMode
                    {
                        GameMode = GameMode.GM_1v1,
                        Lengths = CreateLengths(GameMode.GM_1v1)
                    },
                    new GameLengthPerMode
                    {
                        GameMode = GameMode.GM_2v2_AT,
                        Lengths = CreateLengths(GameMode.GM_2v2_AT)
                    },
                    new GameLengthPerMode
                    {
                        GameMode = GameMode.GM_4v4,
                        Lengths = CreateLengths(GameMode.GM_4v4)
                    },
                    new GameLengthPerMode
                    {
                        GameMode = GameMode.GM_4v4_AT,
                        Lengths = CreateLengths(GameMode.GM_4v4_AT)
                    },
                    new GameLengthPerMode
                    {
                        GameMode = GameMode.FFA,
                        Lengths = CreateLengths(GameMode.FFA)
                    },
                    new GameLengthPerMode
                    {
                        GameMode = GameMode.GM_2v2,
                        Lengths = CreateLengths(GameMode.GM_2v2)
                    },
                }
            };
        }

        private static List<GameLength> CreateLengths(GameMode gameMode)
        {
            var lengths = new List<GameLength>();
            var iterations = gameMode != GameMode.FFA ? 120 : 360;
            for (var i = 0; i <= iterations; i++)
            {
                lengths.Add(new GameLength {passedTimeInSeconds = i * 30});
            }

            return lengths;
        }
    }
}
