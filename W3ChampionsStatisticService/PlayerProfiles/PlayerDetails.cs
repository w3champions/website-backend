using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson.Serialization.Attributes;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles.War3InfoPlayerAkas;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    [BsonIgnoreExtraElements]
    public class PlayerDetails
    {
        public string Id { get; set; }
        public string BattleTag { get; set; }

        public List<RaceWinLoss> WinLosses { get; set; }

        public PersonalSetting[] PersonalSettings { get; set; }

        public Player PlayerAkaData { get; set; }

        public Race GetMainRace()
        {
            var mostGamesRace = WinLosses?
                 .OrderByDescending(x => x.Games)
                 .FirstOrDefault();

            return mostGamesRace != null ? mostGamesRace.Race : Race.RnD;
        }
    }
}
