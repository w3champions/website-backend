using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi.Models;
using NUnit.Framework;
using Swashbuckle.AspNetCore.SwaggerGen;
using W3C.Domain.Maps;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// The pre-check reads its x-proof-hash header itself (no bound parameter, spec §10.3), so API Explorer alone would
/// document a parameterless GET. The Swagger operation filter adds the header to that one operation's document —
/// documentation only, it binds nothing — and leaves every other action alone.
/// </summary>
[TestFixture]
public class ProofHashHeaderOperationFilterTests
{
    [Test]
    public void TheStatusAction_IsDocumentedWithARequiredProofHashHeader()
    {
        var operation = new OpenApiOperation();

        new ProofHashHeaderOperationFilter().Apply(operation, ContextFor(typeof(TemporaryMapsController), nameof(TemporaryMapsController.GetStatus)));

        var parameter = operation.Parameters.Single();
        Assert.That(parameter.Name, Is.EqualTo(TemporaryMapKeys.ProofHashHeaderName));
        Assert.That(parameter.In, Is.EqualTo(ParameterLocation.Header));
        Assert.That(parameter.Required, Is.True);
        Assert.That(parameter.Schema.Type, Is.EqualTo("string"));
        Assert.That(parameter.Schema.Pattern, Is.EqualTo("^[0-9a-f]{64}$"));
        Assert.That(parameter.Description, Does.Contain("64 lowercase hex").And.Contain("unknown"));
    }

    [Test]
    public void TheStatusAction_KeepsTheParametersItAlreadyHas()
    {
        var operation = new OpenApiOperation();
        operation.Parameters.Add(new OpenApiParameter { Name = "existing", In = ParameterLocation.Query });

        new ProofHashHeaderOperationFilter().Apply(operation, ContextFor(typeof(TemporaryMapsController), nameof(TemporaryMapsController.GetStatus)));

        Assert.That(operation.Parameters.Select(p => p.Name), Is.EqualTo(new[] { "existing", TemporaryMapKeys.ProofHashHeaderName }));
    }

    [TestCase(typeof(TemporaryMapsController), nameof(TemporaryMapsController.Upload))]
    [TestCase(typeof(MapsController), nameof(MapsController.GetMaps))]
    public void EveryOtherAction_GetsNoHeaderParameter(Type controller, string action)
    {
        var operation = new OpenApiOperation();

        new ProofHashHeaderOperationFilter().Apply(operation, ContextFor(controller, action));

        Assert.That(operation.Parameters, Is.Empty);
    }

    private static OperationFilterContext ContextFor(Type controller, string action)
        => new(new ApiDescription(), schemaRegistry: null, new SchemaRepository(), controller.GetMethod(action));
}
