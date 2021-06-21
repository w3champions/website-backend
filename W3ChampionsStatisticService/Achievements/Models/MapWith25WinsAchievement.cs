using System;
using System.Collections.Generic;

namespace W3ChampionsStatisticService.Achievements.Models {
    public class MapWith25WinsAchievement: Achievement {
        public Dictionary<string, int> MapWinsCounter;
        public MapWith25WinsAchievement(){
            Id = 0;
            Title = "Win 25 Games On Any Map";
            Caption = "Player has yet to win 25 games on any map.";
            ProgressCurrent = 0;
            ProgressEnd = 25;
            Completed = false;
        }
    }
}