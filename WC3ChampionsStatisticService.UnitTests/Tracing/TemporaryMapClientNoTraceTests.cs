using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using W3C.Domain.MatchmakingService;
using W3C.Domain.Tracing;
using W3C.Domain.UpdateService;

namespace WC3ChampionsStatisticService.Tests.Tracing;

/// <summary>
/// Both clients are [Trace] classes registered as Castle CLASS proxies (<c>AddInterceptedSingleton&lt;T&gt;()</c>).
/// Their methods are not virtual today, so <c>TracingInterceptor</c> never sees them; the moment one becomes
/// virtual, every argument turns into a <c>param.{name}</c> activity tag. A parameter carrying a proofHash or a
/// mapProof (directly, or inside a request DTO) must therefore carry [NoTrace] on the method itself — for a class
/// proxy that is where the interceptor reads it (spec §10.3).
/// </summary>
[TestFixture]
public class TemporaryMapClientNoTraceTests
{
    private static readonly HashSet<string> SecretParameterNames = new(StringComparer.OrdinalIgnoreCase) { "proofHash", "mapProof" };

    private static IEnumerable<TestCaseData> SecretCarryingParameters()
    {
        foreach (var clientType in new[] { typeof(MatchmakingServiceClient), typeof(UpdateServiceClient) })
        {
            foreach (var method in clientType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var parameter in method.GetParameters().Where(CarriesASecret))
                {
                    yield return new TestCaseData(method, parameter)
                        .SetName($"SecretParameter_HasNoTraceAttribute({clientType.Name}.{method.Name}.{parameter.Name})");
                }
            }
        }
    }

    [TestCaseSource(nameof(SecretCarryingParameters))]
    public void SecretParameter_HasNoTraceAttribute(MethodInfo method, ParameterInfo parameter)
    {
        Assert.That(parameter.GetCustomAttribute<NoTraceAttribute>(), Is.Not.Null,
            $"{method.DeclaringType!.Name}.{method.Name}(\"{parameter.Name}\") carries a proofHash or mapProof and must be " +
            "annotated [NoTrace], or TracingInterceptor would record it as a \"param.{name}\" activity tag.");
    }

    [Test]
    public void SecretCarryingParameters_AreFound()
    {
        // Guards the check against going vacuous: GetTemporaryMapStateByProofHash, VerifyTemporaryMapProof,
        // CreateTemporaryMap and MarkTemporaryMapFileRestored all carry one.
        Assert.That(SecretCarryingParameters().Count(), Is.GreaterThanOrEqualTo(4));
    }

    private static bool CarriesASecret(ParameterInfo parameter)
        => SecretParameterNames.Contains(parameter.Name!)
           || parameter.ParameterType.GetProperties().Any(p => SecretParameterNames.Contains(p.Name));
}
