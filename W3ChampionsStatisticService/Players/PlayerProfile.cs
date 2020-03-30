using System.Linq;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.Players
{
    public class PlayerProfile
    {
        public static PlayerProfile Create(string battleTag)
        {
            return new PlayerProfile
            {
                BattleTag = battleTag,
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

        [BsonId]
        public string BattleTag { get; set; }
        public RaceStats RaceStats { get; set; }
        public GameModeStats GameModeStats { get; set; }

        public void RecordWin(Race race, GameMode mode, bool won)
        {
            GameModeStats.RecordGame(mode, won);
            RaceStats.RecordGame(race, won);
        }

        public int TotalLosses => GameModeStats.Sum(g => g.Losses);

        public int TotalWins => GameModeStats.Sum(g => g.Wins);
    }
}