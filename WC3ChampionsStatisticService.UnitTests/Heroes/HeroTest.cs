using System;
using System.Linq;
using NUnit.Framework;
using W3ChampionsStatisticService.Heroes;

namespace WC3ChampionsStatisticService.Tests.Heroes;

using MM = W3C.Domain.MatchmakingService;

[TestFixture]
public class HeroTests
{
    [TestCaseSource(nameof(HeroCases))]
    public void HeroParse(MM.Hero heroData, HeroType expectedType)
    {
        var hero = new Hero(heroData);
        Assert.AreEqual(expectedType, hero.Id);
    }

    public static object[] HeroCases()
    {
        var heroTypes = Enum.GetNames(typeof(HeroType));
        return heroTypes
            .Select(hero =>
                new[]
                {
                    new MM.Hero { icon = $"test-path/hero-{hero.ToLower()}.png", level = 1 },
                    Enum.Parse(typeof(HeroType), hero),
                }
            )
            .ToArray<object>();
    }

    [Test]
    public void InvalidHeroParse()
    {
        var invalidHeroData = new MM.Hero { icon = "test-path/hero-invalid.png", level = 1 };
        var hero = new Hero(invalidHeroData);

        Assert.AreEqual(HeroType.Unknown, hero.Id);
    }
}
