using System.Collections.Generic;
using System.Linq;
using W3C.Contracts.Matchmaking;
namespace W3ChampionsStatisticService.Common.Constants;

public static class MmrConstants
{
    public const int MaxMmr = 3000;

    public static readonly Dictionary<GameMode, int> MaxMmrPerGameMode =
        System.Enum.GetValues(typeof(GameMode))
            .Cast<GameMode>()
            .ToDictionary(gm => gm, gm => MaxMmr);
}