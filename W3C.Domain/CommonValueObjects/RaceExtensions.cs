using System.Collections.Generic;
using W3C.Contracts.GameObjects;

namespace W3C.Domain.CommonValueObjects;

public static class RaceExtensions
{
    static readonly Dictionary<RaceId, Race> raceIdMap = new() {
        { RaceId.RnD, Race.RnD },
        { RaceId.HU, Race.HU },
        { RaceId.OC, Race.OC },
        { RaceId.NE, Race.NE },
        { RaceId.UD, Race.UD }
    };

    public static Race FromRaceId(this Race _, RaceId raceId)
    {
        return raceIdMap[raceId];
    }
}
