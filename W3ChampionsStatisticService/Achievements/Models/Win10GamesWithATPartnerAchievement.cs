using System;
using System.Collections.Generic;

namespace W3ChampionsStatisticService.Achievements.Models {
    public class Win10GamesWithATPartnerAchievement: Achievement {
        public Win10GamesWithATPartnerAchievement() {
            Id = 1;
            Title = "Win 10 Games With an AT Partner";
            Caption = "Player has yet to win 10 games with any AT partner.";
            ProgressCurrent = 0;
            ProgressEnd = 10;
            Completed = false;
        }
    }
}