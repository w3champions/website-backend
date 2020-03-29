using System.Linq;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.Players
{
    public class Player
    {
        [BsonId]
        public string BattleTag { get; set; }
        public RaceStats RaceStats { get; set; } = new RaceStats();
        public GameModeStats GameModeStats { get; set; } = new GameModeStats();

        public Player(string battleTag)
        {
            BattleTag = battleTag;
        }

        public void RecordWin(Race race, GameMode mode, bool won)
        {
            GameModeStats.RecordGame(mode, won);
            RaceStats.RecordGame(race, won);
        }

        public int TotalLosses => GameModeStats.Sum(g => g.Losses);

        public int TotalWins => GameModeStats.Sum(g => g.Wins);
    }
}