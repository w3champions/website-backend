using System.Collections.Generic;
using W3C.Domain.MatchmakingService;

namespace W3ChampionsStatisticService.Matches;

public class PlayerScore(string battleTag,
    UnitScore unitScore,
    List<Hero> heroes,
    HeroScore heroScore,
    ResourceScore resourceScore,
    int teamIndex)
{
    public string BattleTag { get; } = battleTag;
    public UnitScore UnitScore { get; } = unitScore;
    public List<Hero> Heroes { get; } = heroes;
    public HeroScore HeroScore { get; } = heroScore;
    public ResourceScore ResourceScore { get; } = resourceScore;
    public int TeamIndex { get; } = teamIndex;
}
