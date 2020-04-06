using System.Linq;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class PlayerProfile
    {
        public static PlayerProfile Create(string battleTag)
        {
            return new PlayerProfile
            {
                Id = battleTag,
                Name = battleTag.Split("#")[0],
                BattleTag = battleTag.Split("#")[1],
                RaceStats = new RaceStats
                {
                    new RaceStat(Race.HU),
                    new RaceStat(Race.OC),
                    new RaceStat(Race.UD),
                    new RaceStat(Race.NE),
                    new RaceStat(Race.RnD)
                },
                GameModeStats = new GameModeStats
                {
                    new GameModeStat(GameMode.GM_1v1),
                    new GameModeStat(GameMode.GM_2v2),
                    new GameModeStat(GameMode.GM_4v4),
                    new GameModeStat(GameMode.FFA)
                }
            };
        }

        public string Id { get; set; }
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public RaceStats RaceStats { get; set; }
        public GameModeStats GameModeStats { get; set; }

        public void RecordWin(Race race, GameMode mode, bool won, int updatedMmrRating)
        {
            GameModeStats.RecordGame(mode, won, updatedMmrRating);
            RaceStats.RecordGame(race, won);
        }

        public int TotalLosses => GameModeStats.Sum(g => g.Losses);

        public int TotalWins => GameModeStats.Sum(g => g.Wins);
    }
}