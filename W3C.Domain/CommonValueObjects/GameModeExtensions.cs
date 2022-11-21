using System.Linq;
using W3C.Contracts.Matchmaking;

namespace W3C.Domain.CommonValueObjects
{
    public static class GameModeExtensions
    {
        public static GameMode[] RtModes = new GameMode[]
        {
            GameMode.GM_4v4,
            GameMode.GM_2v2
        };

        public static GameMode[] NonMeleeModes = new GameMode[]
        {
            GameMode.GM_FOOTMEN_FRENZY,
        };

        public static bool IsRandomTeam(this GameMode gameMode)
        {
            return RtModes.Contains(gameMode);
        }

        public static bool IsNonMelee(this GameMode gameMode)
        {
            return NonMeleeModes.Contains(gameMode);
        }
    }
}
