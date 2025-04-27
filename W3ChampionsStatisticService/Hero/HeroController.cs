using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace W3ChampionsStatisticService.Heroes;

[ApiController]
[Route("api/hero")]
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
