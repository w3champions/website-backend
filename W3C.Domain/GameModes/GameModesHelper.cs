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

    public const int RaceSplitStartSeason = 2;

    private static readonly Dictionary<GameMode, GameMode> ArrangedTeamVariants = new()
    {
        { GameMode.GM_2v2, GameMode.GM_2v2_AT },
        { GameMode.GM_4v4, GameMode.GM_4v4_AT },
        { GameMode.GM_LEGION_4v4_x20, GameMode.GM_LEGION_4v4_x20_AT },
        { GameMode.GM_DOTA_5ON5, GameMode.GM_DOTA_5ON5_AT },
        { GameMode.GM_DS, GameMode.GM_DS_AT },
        { GameMode.GM_CF, GameMode.GM_CF_AT },
        { GameMode.GM_MINIDOTA_3ON3, GameMode.GM_MINIDOTA_3ON3_AT },
    };

    public static bool IsFfaGameMode(GameMode gameMode)
    {
        return FfaGameModes.Contains(gameMode);
    }

    public static bool IsMeleeGameMode(GameMode gameMode)
    {
        return MeleeGameModes.Contains(gameMode);
    }

    // The arranged-team (AT) variant of a base mode for an AT player; the mode unchanged otherwise.
    public static GameMode ToArrangedTeamVariant(GameMode gameMode, bool isAt)
        => isAt && ArrangedTeamVariants.TryGetValue(gameMode, out var atVariant) ? atVariant : gameMode;

    // 1v1 ladders are keyed per race from RaceSplitStartSeason onward; other modes/seasons are not.
    public static bool UsesRaceInLadderKey(GameMode gameMode, int season)
        => gameMode == GameMode.GM_1v1 && season >= RaceSplitStartSeason;
}
