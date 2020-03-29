using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.Players
{
    public static class PlayerFactory
    {
        public static Player Create(string battleTag)
        {
            return new Player
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
    }
}