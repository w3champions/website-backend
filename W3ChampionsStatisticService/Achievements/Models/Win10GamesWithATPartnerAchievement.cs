using System;
using System.Collections.Generic;

namespace W3ChampionsStatisticService.Achievements.Models {
    public class Win10GamesWithATPartnerAchievement: Achievement {
        public Dictionary<string, int> PartnerWithWins;
        public Win10GamesWithATPartnerAchievement() {
            Id = 1;
            Title = "Win 10 Games With an AT Partner";
            Caption = "Any AT Partner";
            ProgressCurrent = 0;
            ProgressEnd = 10;
            Completed = false;
        }
    }
}