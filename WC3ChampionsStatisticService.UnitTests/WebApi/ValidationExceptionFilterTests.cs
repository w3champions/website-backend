using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NUnit.Framework;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace WC3ChampionsStatisticService.Tests.WebApi;

/// <summary>
/// The global answer to a ValidationException: 400 with an ErrorResult carrying the message. Plan watch item 5 asked
/// whether that answer reaches clients although the filter, unlike its sibling, never set ExceptionHandled: MVC treats a
/// non-null Result as handled, so it always did — the pipeline test below pins that with a real Kestrel round trip, and
/// the flag is now set as well so the two filters read the same.
/// </summary>
[TestFixture]
public class ValidationExceptionFilterTests
{
    internal const string Message = "Invite to another clan still pending";
    internal const string ProbeRoute = "validation-probe";

    [Test]
    public void AValidationException_IsAnsweredAsBadRequest_WithItsMessage_AndMarkedHandled()
    {
        var context = ExceptionContextFor(new ValidationException(Message));

        new ValidationExceptionFilter().OnException(context);

        var result = (BadRequestObjectResult)context.Result!;
        Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(((ErrorResult)result.Value!).Error, Is.EqualTo(Message));
        Assert.That(context.ExceptionHandled, Is.True, "as the sibling HttpRequestExceptionFilter marks its answers");
    }

    [Test]
    public void AnyOtherException_IsLeftForTheNextFilter()
    {
        var context = ExceptionContextFor(new InvalidOperationException(Message));

        new ValidationExceptionFilter().OnException(context);

        Assert.That(context.Result, Is.Null);
        Assert.That(context.ExceptionHandled, Is.False);
    }

    [Test]
    public async Task TheBadRequest_ReachesTheClient_ThroughTheMvcPipeline()
    {
        // The same two global filters as Program.cs, in the same order, in front of a controller that throws.
        await using var host = await LoopbackMvcHost.StartAsync(_ => { }, typeof(ValidationProbeController));

        var response = await host.Client.GetAsync(ProbeRoute);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("{\"error\":\"" + Message + "\"}"));
    }

    private static ExceptionContext ExceptionContextFor(Exception exception)
        => new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>())
        {
            Exception = exception,
        };
}

/// <summary>Throws the ValidationException the pipeline test sends through the global filters. Public: MVC discovers only public controllers.</summary>
[ApiController]
[Route(ValidationExceptionFilterTests.ProbeRoute)]
public class ValidationProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => throw new ValidationException(ValidationExceptionFilterTests.Message);
}
