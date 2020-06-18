using System.Linq;

namespace W3ChampionsStatisticService.CommonValueObjects
{
    public static class GameModeExtensions
    {
        public static GameMode[] RtModes = new GameMode[]
            {
                GameMode.GM_2v2,
                GameMode.GM_4v4
        };

        public static bool IsRandomTeam(this GameMode gameMode)
        {
            return RtModes.Contains(gameMode);
        }
    }
}
