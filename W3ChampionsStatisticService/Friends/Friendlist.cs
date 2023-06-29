using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.Repositories;

namespace W3ChampionsStatisticService.Friends
{
    public class Friendlist : IIdentifiable
    {
        public Friendlist(string battleTag)
        {
            Id = battleTag;
            Friends = new List<string> {};
            ReceivedFriendRequests = new List<string> {};
            BlockedBattleTags = new List<string> {};
            BlockAllRequests = false;
        }

        public string Id { get; set; }
        public List<string> Friends { get; set; }
        public List<string> ReceivedFriendRequests  { get; set; }
        public List<string> BlockedBattleTags  { get; set; }
        public bool BlockAllRequests  { get; set; }
    }
}
