using System.Collections.Generic;

namespace W3ChampionsStatisticService.CommonValueObjects
{
    public enum Race
    {
        RnD = 0,
        HU = 1, 
        OC = 2,
        NE = 4,
        UD = 8,
        Total = 16
    }

    public enum RaceId
    {
        RnD = 0,
        HU = 1,
        OC = 2,
        UD = 3,
        NE = 4,
    }

    public static class Extensions
    {
        static Dictionary<RaceId, Race> raceIdMap = new Dictionary<RaceId, Race>() {
            { RaceId.RnD, Race.RnD },
            { RaceId.HU, Race.HU },
            { RaceId.OC, Race.OC },
            { RaceId.NE, Race.NE },
            { RaceId.UD, Race.UD }
        };

        public static Race FromRaceId(this Race race, RaceId raceId)
        {
            return raceIdMap[raceId];
        }
    }

}