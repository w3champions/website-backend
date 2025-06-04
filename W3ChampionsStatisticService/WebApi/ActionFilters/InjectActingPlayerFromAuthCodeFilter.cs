using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;
using W3C.Domain.Tracing;
using Serilog;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

public class InjectActingPlayerFromAuthCodeFilter(IW3CAuthenticationService authService) : IAsyncActionFilter
{
    public const string ActingPlayerUserKey = "InjectActingPlayerFromAuthCodeFilter:ActingPlayerUser";
    private readonly IW3CAuthenticationService _authService = authService;

    [Trace]
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        try
        {
            var token = GetToken(context.HttpContext.Request.Headers[HeaderNames.Authorization]);
            if (token != null)
            {
                var res = _authService.GetUserByToken(token, false);
                if (res == null)
                {
                    context.Result = new UnauthorizedObjectResult(new ErrorResult("Unable to retrieve user from token"));
                    return;
                }
                // Inject into HttpContext.Items to avoid dependening on the function parameters.
                context.HttpContext.Items[ActingPlayerUserKey] = res;

                // TODO: We should retire this approach as it forces POST instead of GET.
                var actingPlayerContent = context.ActionDescriptor.Parameters.FirstOrDefault(a => a.Name == "actingPlayer");
                if (actingPlayerContent != null)
                {
                    context.ActionArguments["actingPlayer"] = res.BattleTag;
                }

                await next.Invoke();
            }
        }
        catch (Exception ex)
        {
            context.Result = new UnauthorizedObjectResult(new ErrorResult(ex.Message));
        }
    }

    private static string GetToken(StringValues authorization)
    {
        if (AuthenticationHeaderValue.TryParse(authorization, out var headerValue))
        {
            if (headerValue.Scheme == "Bearer")
            {
                return headerValue.Parameter;
            }
        }
        return null;
    }
}
