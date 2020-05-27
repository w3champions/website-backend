using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Authorization
{
    public class BnetAuthFilter : IAsyncActionFilter {
        private readonly IBlizzardAuthenticationService _blizzardAuthenticationService;

        public BnetAuthFilter(IBlizzardAuthenticationService blizzardAuthenticationService)
        {
            _blizzardAuthenticationService = blizzardAuthenticationService;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            context.RouteData.Values.TryGetValue("battleTag", out var battleTag);
            var queryString = HttpUtility.ParseQueryString(context.HttpContext.Request.QueryString.Value);
            if (queryString.AllKeys.Contains("authorization"))
            {
                var auth = queryString["authorization"];
                var res = await _blizzardAuthenticationService.GetUser(auth);

                var btagString = battleTag?.ToString();
                if (
                    res != null
                    && !string.IsNullOrEmpty(btagString)
                    && btagString.StartsWith(res.battletag))
                {
                    await next.Invoke();
                }
            }

            var unauthorizedResult = new UnauthorizedObjectResult("Sorry Hackerboy");
            context.Result = unauthorizedResult;
        }
    }
}