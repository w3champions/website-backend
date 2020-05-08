using System.Collections.Generic;
using System.Linq;
using W3ChampionsStatisticService.W3ChampionsStats.RaceAndWinStats;

namespace W3ChampionsStatisticService.Ports
{
    public class HeroWinRatePerHero
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

        public static HeroWinRatePerHero Create(string heroComboId)
        {
            return new HeroWinRatePerHero
            {
                Id = heroComboId
            };
        }
    }
}