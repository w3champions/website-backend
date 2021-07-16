using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Matches;

namespace W3ChampionsStatisticService.Achievements.Models {
    public class PlayerAchievements {
        [BsonId]
        public string PlayerId {get; set;}
        public List<Achievement> PlayerAchievementList {get; set;}

        new public void Update(Achievement playerAchievement, PlayerOverallStats playerOverallStats, List<Matchup> matches) {
  /*           var firstPartnerTo10Wins = "";
            foreach(Matchup matchup in matches){
                if (matchup.GameMode != GameMode.GM_2v2_AT){continue;}
                if (PlayerDidWin(battleTag, matchup.Teams)){
                    var teamMate = GetPlayerTeamMate(battleTag, matchup.Teams);
                    var hitWinsLimit = AddToWinsCount(achievementProgressCounter, teamMate, 10);
                    if(playerAchievement.ProgressCurrent < playerAchievement.ProgressEnd){
                        playerAchievement.ProgressCurrent = CheckMostWins(achievementProgressCounter);
                    }
                    if(hitWinsLimit){firstPartnerTo10Wins = teamMate; break;}
                    }
                }
                if(firstPartnerTo10Wins != ""){
                    playerAchievement.Caption = $"Player has completed this achievement with {firstPartnerTo10Wins}";
                    playerAchievement.Completed = true;
            } */
        }
    }
}