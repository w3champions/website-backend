using System.Collections.Generic;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.MatchEvents;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;
using W3ChampionsStatisticService.PlayerStats.RaceVersusRaceStats;

namespace W3ChampionsStatisticService.W3ChampionsStats.RaceAndWinStats
{
    public class W3StatsPerMode
    {
        public GameMode GameMode { get; set; }
        public long TotalGames { get; set; }
        public RaceVersusRaceRatio RaceVersusRaceRatio { get; set; }
        public RaceOnMapVersusRaceRatio RaceOnMapVersusRaceRatio { get; set; }

        public static W3StatsPerMode Create(GameMode gameMode)
        {
            return new W3StatsPerMode
            {
                GameMode = gameMode,
                RaceVersusRaceRatio = RaceVersusRaceRatio.Create("Overall#Stats"),
                RaceOnMapVersusRaceRatio = RaceOnMapVersusRaceRatio.Create("Overall#Stats")
            };
        }

        //Todo will not work for 2v2 etc
        public void AddWin(List<PlayerMMrChange> players, string map)
        {
            TotalGames++;

            RaceVersusRaceRatio.AddRaceWin((Race) players[0].race, (Race) players[1].race, players[0].won);
            RaceVersusRaceRatio.AddRaceWin((Race) players[1].race, (Race) players[0].race, players[1].won);

            RaceOnMapVersusRaceRatio.AddMapWin(
                (Race) players[1].race,
                (Race) players[0].race,
                new MapName(map).Name,
                players[1].won);
            RaceOnMapVersusRaceRatio.AddMapWin(
                (Race) players[0].race,
                (Race) players[1].race,
                new MapName(map).Name,
                players[0].won);
        }
    }
}