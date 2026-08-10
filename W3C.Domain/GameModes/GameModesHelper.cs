using System.Collections.Generic;
using W3C.Contracts.Matchmaking;

namespace W3C.Domain.GameModes;

public class GameModesHelper
{
    // Must stay in sync with the game modes whose matchmaking-service definition sets
    // isAnonymous = true, since that is what drives flo's mask_player_names. GM_LTW_FFA is
    // deliberately absent: gm-ltw-ffa.ts sets isAnonymous = false.
    public readonly static List<GameMode> FfaGameModes = [
        GameMode.FFA,
        GameMode.GM_FOOTMEN_FRENZY,
        GameMode.GM_SC_FFA_4,
        GameMode.GM_SC_OZ,
    ];
    public readonly static List<GameMode> MeleeGameModes = [
        GameMode.GM_1v1,
        GameMode.GM_1ON1_TOURNAMENT,
        GameMode.GM_2v2,
        GameMode.GM_2v2_AT,
        GameMode.GM_3v3,
        GameMode.GM_4v4,
        GameMode.GM_4v4_AT,
    ];

    public static bool IsFfaGameMode(GameMode gameMode)
    {
        return FfaGameModes.Contains(gameMode);
    }

    public static bool IsMeleeGameMode(GameMode gameMode)
    {
        return MeleeGameModes.Contains(gameMode);
    }
}
