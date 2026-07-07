using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

/// <summary>
/// Fail-CLOSED inbound auth guard for the endpoint chat-service calls to read a player's
/// friends/blocked lists. An unconfigured secret rejects everyone — it is never treated as an
/// open endpoint by omission — and the <see cref="ChatRelationshipsAuthSettings.Configured"/>
/// check runs FIRST, before any header comparison, so an unset secret can never be satisfied by
/// an empty (or absent) header value.
/// </summary>
public class ChatServiceSecretAuthFilter(ChatRelationshipsAuthSettings settings) : IAsyncActionFilter
{
    public const string HeaderName = "x-chat-relationships-secret";
    private readonly ChatRelationshipsAuthSettings _settings = settings;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!_settings.Configured
            || !context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var provided)
            || !FixedTimeEquals(provided.ToString(), _settings.Secret))
        {
            context.Result = new UnauthorizedResult(); // bare 401 — no detail, no per-request log
            return;
        }
        await next.Invoke();
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
