using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using W3ChampionsStatisticService.Heroes;

namespace WC3ChampionsStatisticService.Tests.Heroes;

using MM = W3C.Domain.MatchmakingService;

[TestFixture]
public class HeroTests
{
    [Test]
    public void HeroFilters()
    {
        var controller = new HeroController();
        var response = controller.GetHeroFilters() as ObjectResult;
        var filters = ((IOrderedEnumerable<HeroFilter>)response!.Value)!.ToList();

        var expectedCount = Enum.GetValues<HeroType>().Count(hero => (int)hero >= 0 && (int)hero < 100);

        Assert.AreEqual(expectedCount, filters?.Count);

        Assert.AreEqual(HeroSource.Unknown, Hero.ParseHeroSource(filters[0].Type));
        Assert.AreEqual("allfilter", filters[0].Name);

        foreach (var filter in filters.Skip(1))
        {
            Assert.AreEqual(HeroSource.Classic, Hero.ParseHeroSource(filter.Type));
        }
    }

    [TestCaseSource(nameof(HeroCases))]
    public void HeroParse(MM.Hero heroData, HeroType expectedType)
    {
        var hero = new Hero(heroData);
        Assert.AreEqual(expectedType, hero.Id);
        Assert.AreEqual(HeroSource.Classic, hero.Source);
        Assert.AreEqual(Enum.GetName(expectedType)!.ToLower(), hero.Name);
    }

    public static object[] HeroCases()
    {
        var heroTypes = Enum.GetValues<HeroType>();
        return heroTypes
            .Where(hero => (int)hero > 0 && (int)hero < 100) // Filter out Unknown, AllFilter and Reforged
            .Select(hero =>
                new object[]
                {
                    new MM.Hero { icon = $"test-path/hero-{Enum.GetName(hero)!.ToLower()}.png", level = 1 },
                    hero,
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
        Assert.AreEqual(HeroSource.Unknown, hero.Source);
    }

    [TestCaseSource(nameof(ReforgedHeroCases))]
    public void ReforgedHeroParse(MM.Hero heroData, HeroType expectedType, string expectedName)
    {
        var hero = new Hero(heroData);

        Assert.AreEqual(expectedType, hero.Id);
        Assert.AreEqual(expectedName, hero.Name);
        Assert.AreEqual(HeroSource.Reforged, hero.Source);
    }

    public static object[] ReforgedHeroCases()
    {
        return Enum.GetValues<HeroType>()
            .Where(hero => (int)hero >= 100) // Reforged hero types
            .Select(hero =>
                new object[]
                {
                    new MM.Hero { icon = $"test-path/reforged-hero-{Enum.GetName(hero)!.ToLower()}.png", level = 1 },
                    hero,
                    Hero.ParseHeroName(hero, HeroSource.Reforged),
                }
            )
            .ToArray<object>();
    }
}
