namespace W3ChampionsStatisticService.Extensions
{
    public static class HeroIconsExtensions
    {
        public static string ParseReforgedName(this string heroIcon)
        {
            if (string.IsNullOrEmpty(heroIcon)) {
                return "unknown";
            }

            if (heroIcon == "jainasea") return "archmage";
            if (heroIcon == "thrallchampion") return "farseer";
            if (heroIcon == "fallenkingarthas") return "deathknight";
            if (heroIcon == "cenariusnightmare") return "keeperofthegrove";
            return heroIcon;
        }
    }
}
