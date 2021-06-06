using System;
using System.Collections.Generic;

namespace W3ChampionsStatisticService.Achievements.Models {
    public class MapWith25WinsAchievement: Achievement {
        public Dictionary<string, int> MapWinsCounter;
        public MapWith25WinsAchievement(){
            Id = 0;
            Title = "Win 25 Games On Any Map";
            Caption = "";
            ProgressCurrent = 0;
            ProgressEnd = 25;
            Completed = false;
        }
    }
}