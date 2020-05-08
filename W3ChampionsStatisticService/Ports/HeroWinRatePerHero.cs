using System.Collections.Generic;
using System.Linq;

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

        public List<HeroWinRate> WinRates { get; set; }
    }
}