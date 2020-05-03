using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace W3ChampionsStatisticService.Ladder
{
    public class PlayerOverview1v1 : WinLoss
    {
        public static PlayerOverview1v1 Create(string id, string nameTag, int gateWay, GameMode gameMode)
        {
            return new PlayerOverview1v1
            {
                Id = id,
                GateWay = gateWay,
                GameMode = gameMode,
                Name = nameTag.Split("#")[0],
                BattleTag = nameTag.Split("#")[1]
            };
        }

        public string Id { get; set; }
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public int MMR { get; set; }
        public int GateWay { get; set; }
        public GameMode GameMode { get; set; }

        public void RecordWin(bool won, int newMmr)
        {
            MMR = newMmr;
            RecordWin(won);
        }
    }
}