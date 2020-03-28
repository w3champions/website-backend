namespace W3ChampionsStatisticService.Matches
{
    public class Player
    {
        public Player(int wins, int losses, int oldMmr, int newMmr, string name, string battleTag)
        {
            Wins = wins;
            Losses = losses;
            OldMmr = oldMmr;
            NewMmr = newMmr;
            BattleTag = battleTag;
            Name = name;
        }

        public int Wins { get; set; }
        public int Losses { get; set; }
        public int OldMmr { get; set; }
        public int NewMmr { get; set; }
        public string BattleTag { get; set; }
        public string Name { get; set; }
    }
}