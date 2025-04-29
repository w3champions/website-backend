using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace W3ChampionsStatisticService.Heroes;

// Cache for 7 days, this isn't likely to change.
[ApiController]
[Route("api/hero")]
[ResponseCache(Duration = 60 * 60 * 24 * 7, VaryByHeader = "HeroFilterVersion")]
public class HeroController() : ControllerBase
{
    [HttpGet("filters")]
    public IActionResult GetHeroFilters()
    {
        var heroValues = Enum.GetValues(typeof(HeroType)).Cast<HeroType>();
        var result = heroValues
            .Where(hero => (int)hero >= 0 && (int)hero < 100) // Non-classic hero ids start from 100, unknown is -1
            .Select(hero => new HeroFilter(hero))
            .OrderBy(hero => hero.Type);

        return Ok(result);
    }
}
