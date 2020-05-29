using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.WebApi.ActionFilters
{
    public class InjectBattleTagFromAuthCodeFilter : IAsyncActionFilter {
        private readonly IBlizzardAuthenticationService _blizzardAuthenticationService;

        public InjectBattleTagFromAuthCodeFilter(IBlizzardAuthenticationService blizzardAuthenticationService)
        {
            _blizzardAuthenticationService = blizzardAuthenticationService;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var queryString = HttpUtility.ParseQueryString(context.HttpContext.Request.QueryString.Value);
            if (queryString.AllKeys.Contains("authorization"))
            {
                var auth = queryString["authorization"];
                var res = await _blizzardAuthenticationService.GetUser(auth);

                var actingPlayerContent = context.ActionDescriptor.Parameters.FirstOrDefault(a => a.Name == "actingPlayer");
                if (actingPlayerContent != null)
                {
                    context.ActionArguments["actingPlayer"] = res.battletag;
                    await next.Invoke();
                }
            }

            var unauthorizedResult = new UnauthorizedObjectResult(new { error = "Sorry H4ckerb0i"});
            context.Result = unauthorizedResult;
        }
    }
}