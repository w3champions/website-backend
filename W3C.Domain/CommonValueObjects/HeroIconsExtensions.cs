namespace W3C.Domain.CommonValueObjects;

public static class HeroIconsExtensions
{
    public static string ParseReforgedName(this string heroIcon)
    {
        if (string.IsNullOrEmpty(heroIcon))
        {
            return "unknown";
        }

        if (heroIcon == "jainasea") return "archmage";
        if (heroIcon == "thrallchampion") return "farseer";
        if (heroIcon == "fallenkingarthas") return "deathknight";
        if (heroIcon == "cenariusnightmare") return "keeperofthegrove";
        return heroIcon;
    }
}
