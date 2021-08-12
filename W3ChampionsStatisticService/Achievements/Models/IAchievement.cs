using System.Collections.Generic;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Matches;
using MongoDB.Bson;

namespace W3ChampionsStatisticService.Achievements.Models {
    public interface IAchievement {
        public long Id {get; set;}
        public string Type {get; set;}
        public string Title {get; set;}
        public string Caption {get; set;}
        public long ProgressCurrent {get; set;}
        public long ProgressEnd {get; set;}
        public bool Completed {get; set;}
        public Dictionary<string,int> Counter {get; set;}
    }
}