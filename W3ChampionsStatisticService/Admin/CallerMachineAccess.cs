using Microsoft.AspNetCore.Http;
using W3C.Contracts.Admin.Permission;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.Admin;

/// <summary>Machine-ban access of the acting moderator, shared by the controllers that forward it to matchmaking.</summary>
internal static class CallerMachineAccess
{
    /// <summary>
    /// True only when the token that InjectActingPlayerAuthCode validated grants AdminModeration. Never bound from the
    /// request. Throws like GetActingPlayerUser when the action has no InjectActingPlayerAuthCode.
    /// </summary>
    public static bool Of(HttpContext context) =>
        InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(context)?.Permissions?.Contains(EPermission.AdminModeration) == true;
}
