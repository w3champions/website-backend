using System.Collections.Generic;
using System.Linq;

namespace W3ChampionsStatisticService.W3ChampionsStats.HeroWinrate
{
    public class OverallHeroWinRatePerHero
    {
        public void RecordGame(bool won, string opponentHeroCombo)
        {
            var heroWinrate = WinRates.SingleOrDefault(w => w.HeroCombo == opponentHeroCombo);
            if (heroWinrate == null)
            {
                WinRates.Add(HeroWinRate.Create(opponentHeroCombo));
            }

            heroWinrate = WinRates.Single(w => w.HeroCombo == opponentHeroCombo);

            heroWinrate.RecordGame(won);
        }

        public List<HeroWinRate> WinRates { get; set; } = new List<HeroWinRate>();
        public string Id { get; set; }

        public static OverallHeroWinRatePerHero Create(string heroComboId)
        {
            return new OverallHeroWinRatePerHero
            {
                Id = heroComboId
            };
        }
    }
}