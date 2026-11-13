using System.Collections.Generic;
using W3C.Contracts.Matchmaking;

namespace W3C.Domain.GameModes;

public class GameModesHelper
{
    public readonly static List<GameMode> FfaGameModes = [GameMode.FFA, GameMode.GM_SC_FFA_4, GameMode.GM_SC_OZ];
    public readonly static List<GameMode> MeleeGameModes = [
        GameMode.GM_1v1,
        GameMode.GM_1ON1_TOURNAMENT,
        GameMode.GM_2v2,
        GameMode.GM_2v2_AT,
        GameMode.GM_3v3,
        GameMode.GM_4v4,
        GameMode.GM_4v4_AT,
    ];

    // Modes that display a map's current name from matchmaking instead of the name stored at match time.
    // Broader than MeleeGameModes, which drives hero and race stats and must not change.
    public readonly static List<GameMode> CurrentMapNameGameModes = [
        GameMode.GM_1v1,
        GameMode.GM_2v2,
        GameMode.GM_3v3,
        GameMode.GM_4v4,
        GameMode.FFA,
        GameMode.GM_2v2_AT,
        GameMode.GM_4v4_AT,
        GameMode.GM_1ON1_TOURNAMENT,
        GameMode.GM_ATR_1ON1,
        GameMode.GM_ROC_1ON1,
        GameMode.GM_PTR_1ON1,
    ];

    public static bool IsFfaGameMode(GameMode gameMode)
    {
        return FfaGameModes.Contains(gameMode);
    }

    public static bool IsMeleeGameMode(GameMode gameMode)
    {
        return MeleeGameModes.Contains(gameMode);
    }

    public static bool UsesCurrentMapName(GameMode gameMode)
    {
        return CurrentMapNameGameModes.Contains(gameMode);
    }
}
