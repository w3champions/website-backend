using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.Authorization;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace W3ChampionsStatisticService.WebApi.ActionFilters
{
    public class CheckIfBattleTagIsAdminFilter : IAsyncActionFilter {
        private readonly IW3CAuthenticationService _authService;

        public CheckIfBattleTagIsAdminFilter(IW3CAuthenticationService authService)
        {
            _authService = authService;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var queryString = HttpUtility.ParseQueryString(context.HttpContext.Request.QueryString.Value);
            if (queryString.AllKeys.Contains("authorization"))
            {
                var auth = queryString["authorization"];
                var res = await _authService.GetUserByToken(auth);
                if (
                    res != null
                    && !string.IsNullOrEmpty(res.Battletag)
                    && Admins.ApprovedAdmins.Any(x => x.ToLower() == res.Battletag.ToLower()))
                {
                    context.ActionArguments["battleTag"] = res.Battletag;
                    await next.Invoke();
                }
            }

            var unauthorizedResult = new UnauthorizedObjectResult(new ErrorResult("Sorry H4ckerb0i"));
            context.Result = unauthorizedResult;
        }
    }
}