﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Ports;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Clans;

[ApiController]
[Route("api/memberships")]
[Trace]
public class MembershipController(IClanRepository clanRepository) : ControllerBase
{
    private readonly IClanRepository _clanRepository = clanRepository;

    [HttpGet("{membershipId}")]
    public async Task<IActionResult> GetMembership(string membershipId)
    {
        var memberShip = await _clanRepository.LoadMemberShip(membershipId);
        return Ok(memberShip);
    }
}
