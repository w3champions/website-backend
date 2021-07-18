using System.Collections.Generic;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.CommonValueObjects;

namespace W3ChampionsStatisticService.Achievements.Models {
    public class Win10GamesWithATPartnerAchievement: Achievement {
        public Win10GamesWithATPartnerAchievement() {
            Id = 1;
            Title = "Win 10 Games With an AT Partner";
            Caption = "Player has yet to win 10 games with any AT partner.";
            ProgressCurrent = 0;
            ProgressEnd = 10;
            Completed = false;
            Counter = new Dictionary<string, int>();
        }
        override public void Update(PlayerOverallStats playerOverallStats, List<Matchup> matches) {
            var battleTag = playerOverallStats.BattleTag;
            var firstPartnerTo10Wins = "";
            foreach(Matchup matchup in matches){
                if (matchup.GameMode != GameMode.GM_2v2_AT){continue;}
                if (base.PlayerDidWin(battleTag, matchup.Teams)){
                    var teamMate = base.GetPlayerTeamMate(battleTag, matchup.Teams);
                    var hitWinsLimit = base.AddToWinsCount(Counter, teamMate, 10);
                    if(ProgressCurrent < ProgressEnd){
                        ProgressCurrent = base.CheckMostWins(Counter);
                    }
                    if(hitWinsLimit){firstPartnerTo10Wins = teamMate; break;}
                    }
                }
                if(firstPartnerTo10Wins != ""){
                    Caption = $"Player has completed this achievement with {firstPartnerTo10Wins}";
                    Completed = true;
            }
        }
    }
}