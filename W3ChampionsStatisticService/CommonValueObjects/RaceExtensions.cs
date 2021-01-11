using System.Collections.Generic;

namespace W3ChampionsStatisticService.CommonValueObjects
{
    public static class RaceExtensions
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
