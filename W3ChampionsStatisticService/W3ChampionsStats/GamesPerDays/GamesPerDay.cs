using System;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays
{
    public class GamesPerDay : IIdentifiable
    {
        public DateTimeOffset Date { get; set; }
        public long GamesPlayed { get; set; }
        public string Id => $"{GateWay}_{GameMode}_{Date:yyyy-MM-dd}";
        public GameMode GameMode { get; set; }
        public GateWay GateWay { get; set; }

        public static GamesPerDay Create(DateTimeOffset endTime, GameMode gameMode, GateWay gateWay)
        {
            return new GamesPerDay
            {
                Date = endTime,
                GameMode = gameMode,
                GateWay = gateWay
            };
        }

        public void AddGame()
        {
            GamesPlayed++;
        }
    }
}