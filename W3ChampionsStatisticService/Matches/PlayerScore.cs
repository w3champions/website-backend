using System.Collections.Generic;
using W3ChampionsStatisticService.PadEvents;

namespace W3ChampionsStatisticService.Matches
{
    public class PlayerScore
    {
        public PlayerScore(
            string battleTag,
            OverallScore overallScore,
            UnitScore unitScore,
            List<Hero> heroes,
            HeroScore heroScore,
            ResourceScore resourceScore)
        {
            BattleTag = battleTag;
            OverallScore = overallScore;
            UnitScore = unitScore;
            Heroes = heroes;
            HeroScore = heroScore;
            ResourceScore = resourceScore;
        }

        public string BattleTag { get; }
        public OverallScore OverallScore { get; }
        public UnitScore UnitScore { get; }
        public List<Hero> Heroes { get; }
        public HeroScore HeroScore { get; }
        public ResourceScore ResourceScore { get; }
    }
}