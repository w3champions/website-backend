using System;
using System.Collections.Generic;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.PlayerProfiles.Search
{
    public class PlayerGlobalSearch
    {
        public PlayerGlobalSearch (
            string battleTag,
            string name,
            ProfilePicture picture,
            int[] seasons,
            List<LatestSeasonLeague> latestSeasonLeague)
        {
          BattleTag = battleTag;
          Name = name;
          Picture = picture;
          ParticipatedInSeasons = seasons;
          LatestSeasonLeague = latestSeasonLeague;
          Array.Sort(seasons);
          LatestSeasonLeague.Sort();
        }
        public string BattleTag { get; set; }
        public string Name { get; set; }
        public ProfilePicture Picture { get; set; } = ProfilePicture.Default();
        public int[] ParticipatedInSeasons { get; set; }
        public List<LatestSeasonLeague> LatestSeasonLeague { get; set; }
    }

    public class LatestSeasonLeague : IComparable
    {
        public int League { get; set; }
        public GameMode GameMode { get; set; }
        public int Season { get; set; }
        public int RankNumber { get; set; }

        public int CompareTo(object otherObject) {
          LatestSeasonLeague otherLatestSeasonLeague = otherObject as LatestSeasonLeague;
          return otherLatestSeasonLeague.Season.CompareTo(this.Season);
        }
    }
}