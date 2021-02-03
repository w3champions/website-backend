using System.Collections.Generic;

namespace W3ChampionsStatisticService.Achievements.Models {
    class PlayerMatchupPartnerData {
        // dictionary[battleTag] => { "Wins": 0, "Losses": 0, "EstimatedGameTimeInSeconds": 0}
        public Dictionary<string,PlayerAndTeamMateRecordData> PartnersAndRecord { get; set; }
    }
}