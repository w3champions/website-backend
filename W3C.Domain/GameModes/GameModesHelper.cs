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

    /// <summary>
    /// Modes with a separate rating per race, mirroring splitLadderByRace on the
    /// matchmaking service's game modes. Every other mode carries one rating that
    /// follows the player whatever race they pick, so the race a match was
    /// recorded under says nothing about which rating it belongs to.
    /// </summary>
    public readonly static List<GameMode> RaceSplitGameModes = [
        GameMode.GM_1v1,
        GameMode.GM_PTR_1ON1,
    ];

    public static bool IsRaceSplitGameMode(GameMode gameMode)
    {
        return RaceSplitGameModes.Contains(gameMode);
    }

    public static bool IsFfaGameMode(GameMode gameMode)
    {
        return FfaGameModes.Contains(gameMode);
    }

    public static bool IsMeleeGameMode(GameMode gameMode)
    {
        return MeleeGameModes.Contains(gameMode);
    }
}
