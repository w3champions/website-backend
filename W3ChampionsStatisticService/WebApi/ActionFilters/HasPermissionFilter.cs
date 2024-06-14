using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;
using W3C.Contracts.Admin.Permission;
using Serilog;
using W3C.Domain.IdentificationService;

namespace W3ChampionsStatisticService.WebApi.ActionFilters;

[AttributeUsage(AttributeTargets.Method)]
public class HasPermissionFilter : Attribute, IAsyncActionFilter
{
    public EPermission Permission { get; set; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        try
        {
            var client = context.HttpContext.RequestServices.GetRequiredService<IdentificationServiceClient>();
            context.ActionArguments.TryGetValue("battleTag", out var battleTag);
            if (battleTag != null && !await client.HasPermission(Permission, battleTag.ToString()))
            {
                await next.Invoke();
            
            }
            else
            {
                Log.Error($"Permission {Permission} missing for {battleTag}.");
                context.Result = new UnauthorizedObjectResult(new ErrorResult("Permission missing"));
            }

        } catch (Exception ex)
        {
            Log.Error($"Permission {Permission} couldn't check {ex.Message}.");
            context.Result = new UnauthorizedObjectResult(new ErrorResult("Sorry H4ckerb0i"));
        }
    }
}
