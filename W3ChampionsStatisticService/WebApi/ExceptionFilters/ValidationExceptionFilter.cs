using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace W3ChampionsStatisticService.WebApi.ExceptionFilters
{
    public class ValidationExceptionFilter : IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            if (!(context.Exception is ValidationException validationException)) return;

            var result = new OkObjectResult(new { error = validationException.Message });
            result.StatusCode = 400;
            context.Result = result;
        }
    }
}