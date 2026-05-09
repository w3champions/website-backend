using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3ChampionsStatisticService.Clans.Commands;
using W3ChampionsStatisticService.Services;
using W3ChampionsStatisticService.WebApi.ActionFilters;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Clans;

[ApiController]
[Route("api/clans")]
[Trace]
public class ClanController(
    ClanCommandHandler clanCommandHandler,
    IBattleTagResolver battleTagResolver) : ControllerBase
{
    private readonly ClanCommandHandler _clanCommandHandler = clanCommandHandler;
    private readonly IBattleTagResolver _battleTagResolver = battleTagResolver;

    [HttpPost]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> CreateClan(
        [FromBody] CreateClanDto clanDto,
        string actingPlayer)
    {
        var clan = await _clanCommandHandler.CreateClan(clanDto.ClanName, clanDto.ClanAbbrevation, actingPlayer);
        return Ok(clan);
    }

    [HttpGet("{clanId}")]
    public async Task<IActionResult> GetClan(string clanId)
    {
        var clan = await _clanCommandHandler.LoadClan(clanId);
        if (clan == null)
        {
            return NotFound();
        }

        return Ok(clan);
    }

    [HttpGet]
    public async Task<IActionResult> GetClanForPlayer(string battleTag)
    {
        var clan = await _clanCommandHandler.GetClanForPlayer(battleTag);
        return Ok(clan);
    }

    [HttpDelete("{clanId}")]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> DeleteClan(
        string clanId,
        string actingPlayer)
    {
        await _clanCommandHandler.DeleteClan(clanId, actingPlayer);
        return Ok();
    }

    [HttpPost("{clanId}/invites")]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> InviteToClan(
        string clanId,
        string actingPlayer,
        [FromBody] PlayerDto playerDto)
    {
        var (canonical, error) = await ResolveOrReject(playerDto.PlayerBattleTag);
        if (error != null) return error;
        await _clanCommandHandler.InviteToClan(canonical, clanId, actingPlayer);
        return Ok();
    }

    [HttpDelete("{clanId}/invites")]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> RevokeInvitationToClan(
        string clanId,
        string actingPlayer,
        [FromBody] PlayerDto playerDto)
    {
        var (canonical, error) = await ResolveOrReject(playerDto.PlayerBattleTag);
        if (error != null) return error;
        await _clanCommandHandler.RevokeInvitationToClan(canonical, clanId, actingPlayer);
        return Ok();
    }

    [HttpPut("{clanId}/chieftain")]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> SwitchChieftain(
        string clanId,
        string actingPlayer,
        [FromBody] PlayerDto playerDto)
    {
        var (canonical, error) = await ResolveOrReject(playerDto.PlayerBattleTag);
        if (error != null) return error;
        var clan = await _clanCommandHandler.SwitchChieftain(canonical, clanId, actingPlayer);
        return Ok(clan);
    }


    [HttpPost("{clanId}/shamans")]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> AddShamanToClan(
        string clanId,
        string actingPlayer,
        [FromBody] PlayerDto playerDto)
    {
        var (canonical, error) = await ResolveOrReject(playerDto.PlayerBattleTag);
        if (error != null) return error;
        var clan = await _clanCommandHandler.AddShamanToClan(canonical, clanId, actingPlayer);
        return Ok(clan);
    }

    [HttpDelete("{clanId}/shamans/{shamanId}")]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> RemoveShamanFromClan(
        string clanId,
        string actingPlayer,
        string shamanId)
    {
        var (canonical, error) = await ResolveOrReject(shamanId);
        if (error != null) return error;
        var clan = await _clanCommandHandler.RemoveShamanFromClan(canonical, clanId, actingPlayer);
        return Ok(clan);
    }

    [HttpDelete("{clanId}/members/{battleTag}")]
    [BearerCheckIfBattleTagBelongsToAuth]
    public async Task<IActionResult> RevokeInvitationToClan(
        string clanId,
        string battleTag)
    {
        var clan = await _clanCommandHandler.LeaveClan(clanId, battleTag);
        return Ok(clan);
    }

    [HttpPut("{clanId}/members/{battleTag}")]
    [InjectActingPlayerAuthCode]
    public async Task<IActionResult> KickPlayerFromClan(
        string clanId,
        string actingPlayer,
        string battleTag)
    {
        var (canonical, error) = await ResolveOrReject(battleTag);
        if (error != null) return error;
        var clan = await _clanCommandHandler.KickPlayer(canonical, clanId, actingPlayer);
        return Ok(clan);
    }

    [HttpPut("{clanId}/invites/{battleTag}")]
    [BearerCheckIfBattleTagBelongsToAuth]
    public async Task<IActionResult> AcceptInvite(string clanId, string battleTag)
    {
        var clan = await _clanCommandHandler.AcceptInvite(battleTag, clanId);
        return Ok(clan);
    }

    [HttpDelete("{clanId}/invites/{battleTag}")]
    [BearerCheckIfBattleTagBelongsToAuth]
    public async Task<IActionResult> RejectInvite(string clanId, string battleTag)
    {
        var clan = await _clanCommandHandler.RejectInvite(clanId, battleTag);
        return Ok(clan);
    }

    private async Task<(string canonical, IActionResult error)> ResolveOrReject(string input)
    {
        var canonical = await _battleTagResolver.ResolveCanonical(input);
        if (canonical == null)
            return (null, BadRequest(new { error = "user_not_found", input }));
        if (canonical != input)
            return (null, BadRequest(new { error = "non_canonical_battletag", input, canonical }));
        return (canonical, null);
    }
}
