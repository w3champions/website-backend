using System.Collections.Generic;
using W3ChampionsStatisticService.Achievements.Models;

namespace W3ChampionsStatisticService.Achievements {
    public static class AchievementEvaluator {
        public static List<Achievement> AllActiveAchievements;

        static AchievementEvaluator() {
            AllActiveAchievements = new List<Achievement>();
            AllActiveAchievements.Add(new MapWith25WinsAchievement());
            AllActiveAchievements.Add(new Win10GamesWithATPartnerAchievement());
            AllActiveAchievements.Add(new WinGamesWithEveryTavernHero());
            AllActiveAchievements.Add(new Win30GamesWithArmyUnder50());
        }
    }
}