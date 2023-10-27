using System.Collections.Generic;
using W3C.Contracts.Matchmaking;

namespace W3C.Domain.GameModes;

public class GameModesHelper
{
    public readonly static List<GameMode> FfaGameModes  = new () { GameMode.FFA, GameMode.GM_SC_FFA_4 };

    public static bool IsFfaGameMode(GameMode gameMode)
    {
        return FfaGameModes.Contains(gameMode);
    }
}
