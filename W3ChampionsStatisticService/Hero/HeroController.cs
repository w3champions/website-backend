using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace W3ChampionsStatisticService.Heroes;

// Cache for 7 days, this isn't likely to change.
[ApiController]
[Route("api/hero")]
[ResponseCache(Duration = 60 * 60 * 24 * 7)]
public class HeroController() : ControllerBase
{
    [HttpGet("filters")]
    public IActionResult GetHeroFilters()
    {
        var heroValues = Enum.GetValues(typeof(HeroType)).Cast<HeroType>();
        var result = heroValues
            .Where(hero => hero != HeroType.Unknown)
            .Select(hero => new HeroFilter { Type = hero, Name = Enum.GetName(hero)?.ToLower() })
            .OrderBy(hero => hero.Type);

        return Ok(result);
    }
}
