using System.Collections.Generic;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.PlayerProfiles.War3InfoPlayerAkas
{
    public class Akas // Also Known As
    {
        public Akas()
        {
            AllAkas = new Dictionary<string, PlayerAka>();
        }

        public Dictionary<string, PlayerAka> AllAkas { get; set; }
    }
}
