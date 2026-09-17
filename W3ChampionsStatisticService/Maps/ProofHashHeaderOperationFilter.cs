using System.Reflection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using W3C.Domain.Maps;

namespace W3ChampionsStatisticService.Maps;

/// <summary>
/// Documents the pre-check's input in Swagger. <see cref="TemporaryMapsController.GetStatus"/> reads its
/// <c>x-proof-hash</c> header itself instead of binding a parameter (§10.3: no bound argument that MVC or an
/// interceptor could render), so API Explorer alone describes a parameterless GET. This adds the header to that one
/// operation's document and nothing else: it binds nothing, and the action validates the header as before.
/// </summary>
public sealed class ProofHashHeaderOperationFilter : IOperationFilter
{
    private static readonly MethodInfo StatusAction =
        typeof(TemporaryMapsController).GetMethod(nameof(TemporaryMapsController.GetStatus))!;

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!StatusAction.Equals(context.MethodInfo))
        {
            return;
        }

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = TemporaryMapKeys.ProofHashHeaderName,
            In = ParameterLocation.Header,
            Required = true,
            Description = "The proofHash of the map file, 64 lowercase hex characters, sent as a request header and never in " +
                          "the URL. A missing, repeated or malformed value is answered 404 { state: \"unknown\" }.",
            Schema = new OpenApiSchema
            {
                Type = "string",
                Pattern = "^[0-9a-f]{64}$",
                MinLength = TemporaryMapKeys.ProofHashHexLength,
                MaxLength = TemporaryMapKeys.ProofHashHexLength,
            },
        });
    }
}
