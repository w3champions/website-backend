using System;
using System.Collections.Generic;

namespace W3ChampionsStatisticService.Achievements.Models {
    public class Play100GamesInASingleSeason: Achievement {
        public Play100GamesInASingleSeason() {
            Id = 2;
            Title = "Play At Least 100 Games In A Single Season";
            Caption = "Player has yet to play 100 games in a single season.";
            ProgressCurrent = 0;
            ProgressEnd = 100;
            Completed = false;
        }
    }
}