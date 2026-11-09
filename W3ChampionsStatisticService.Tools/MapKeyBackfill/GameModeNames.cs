using System;
using System.Collections.Generic;

namespace W3ChampionsStatisticService.Tools.MapKeyBackfill;

// The Tools project has no project references, so this mirrors W3C.Contracts.Matchmaking.GameMode
// (names and stored int values). A unit test keeps the two in sync.
public static class GameModeNames
{
    public const int Undefined = 0;
    public const int OneVsOne = 1;

    public static readonly IReadOnlyDictionary<string, int> ValuesByName = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["Undefined"] = 0,
        ["GM_1v1"] = 1,
        ["GM_2v2"] = 2,
        ["GM_3v3"] = 3,
        ["GM_4v4"] = 4,
        ["FFA"] = 5,
        ["GM_2v2_AT"] = 6,
        ["GM_4v4_AT"] = 8,
        ["CUSTOM"] = 9,
        ["GM_1ON1_TOURNAMENT"] = 11,
        ["GM_FOOTMEN_FRENZY"] = 101,
        ["GM_LEGION_4v4_x3"] = 201,
        ["GM_LEGION_4v4_x20"] = 202,
        ["GM_LEGION_1v1_x20"] = 203,
        ["GM_LEGION_4v4_x20_AT"] = 204,
        ["GM_LEGION_2v2_x20"] = 205,
        ["GM_ROC_1ON1"] = 301,
        ["GM_LTW_1ON1"] = 401,
        ["GM_LTW_FFA"] = 402,
        ["GM_FROSTCRAFT_4ON4"] = 501,
        ["GM_ATR_1ON1"] = 601,
        ["GM_BANJOBALL_4ON4"] = 701,
        ["GM_PTR_1ON1"] = 801,
        ["GM_DOTA_5ON5"] = 901,
        ["GM_DOTA_5ON5_AT"] = 902,
        ["GM_SC_FFA_4"] = 1001,
        ["GM_SC_OZ"] = 1002,
        ["GM_DS"] = 1101,
        ["GM_DS_AT"] = 1102,
        ["GM_WARHAMMER_1ON1"] = 1201,
        ["GM_CF"] = 1301,
        ["GM_CF_AT"] = 1302,
        ["GM_RISK_EUROPE"] = 1401,
        ["GM_MINIDOTA_3ON3"] = 1501,
        ["GM_MINIDOTA_3ON3_AT"] = 1502
    };

    // Mirrors W3C.Domain.GameModes.GameModesHelper.CurrentMapNameGameModes: the modes whose entries display the
    // map's current matchmaking name instead of the stored one. A unit test keeps the two in sync.
    public static readonly IReadOnlySet<int> CurrentMapNameModes = new HashSet<int> { 1, 2, 3, 4, 5, 6, 8, 11, 301, 601, 801 };

    public static bool UsesCurrentMapName(int gameMode) => CurrentMapNameModes.Contains(gameMode);

    public static int Parse(string name)
    {
        if (name == null || !ValuesByName.TryGetValue(name, out var value))
        {
            throw new FormatException($"Unknown game mode '{name}'.");
        }

        return value;
    }

    public static string NameOf(int? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        foreach (var pair in ValuesByName)
        {
            if (pair.Value == value.Value)
            {
                return pair.Key;
            }
        }

        return value.Value.ToString();
    }
}
