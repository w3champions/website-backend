using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson;

namespace W3ChampionsStatisticService.Achievements.Models
{
    public class Achievement {
        public string Title {get; set;}
        public string caption {get; set;}
        public int Id {get; set;}
        public string Type  {get; set;}// ex: map, player, etc
        public string[] Rules {get; set;}// ex: map == some_map && wins =< 25
    }
}