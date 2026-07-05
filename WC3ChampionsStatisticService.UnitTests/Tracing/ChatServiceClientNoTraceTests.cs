using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using W3C.Domain.ChatService;
using W3C.Domain.Tracing;

namespace WC3ChampionsStatisticService.Tests.Tracing;

/// <summary>
/// Regression guard for the interceptor/proxy tracing setup: because <c>ChatServiceClient</c> is
/// registered via <c>AddInterceptedSingleton&lt;IChatServiceClient, ChatServiceClient&gt;()</c>,
/// <see cref="TracingInterceptor"/> reads <see cref="NoTraceAttribute"/> off the INTERFACE's
/// <see cref="MethodInfo.GetParameters"/> (the invoked proxy method), not the concrete class's.
/// Any bearer-token-carrying parameter added to <see cref="IChatServiceClient"/> without
/// [NoTrace] would silently leak the token into tracing activity tags
/// (see TracingInterceptor.Intercept, the "param.{name}" tag). This test fails loudly instead.
/// </summary>
[TestFixture]
public class ChatServiceClientNoTraceTests
{
    // Matches this codebase's naming convention for "this parameter carries a bearer token":
    // IChatServiceClient/ChatServiceClient use "authorization" (see GetLoungeMutes,
    // GetChatRoomMessages, etc.), ModerationController's action methods use "authToken".
    private static readonly HashSet<string> BearerTokenParameterNames = ["authorization", "authToken"];

    private static IEnumerable<TestCaseData> AuthorizationParameters()
    {
        foreach (MethodInfo method in typeof(IChatServiceClient).GetMethods())
        {
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                if (BearerTokenParameterNames.Contains(parameter.Name))
                {
                    yield return new TestCaseData(method, parameter)
                        .SetName($"AuthorizationParameter_HasNoTraceAttribute({method.Name})");
                }
            }
        }
    }

    [TestCaseSource(nameof(AuthorizationParameters))]
    public void AuthorizationParameter_HasNoTraceAttribute(MethodInfo method, ParameterInfo parameter)
    {
        Assert.That(parameter.GetCustomAttribute<NoTraceAttribute>(), Is.Not.Null,
            $"{method.DeclaringType!.Name}.{method.Name}(\"{parameter.Name}\") carries a bearer " +
            "token and must be annotated [NoTrace] on the interface, or TracingInterceptor will " +
            "record it as a \"param.{name}\" activity tag.");
    }

    [Test]
    public void AuthorizationParameters_AtLeastOneExistsOnInterface()
    {
        // Guards against this whole check silently going vacuous (e.g. if a future refactor
        // renames every bearer-token parameter away from the names tracked above).
        Assert.That(AuthorizationParameters(), Is.Not.Empty,
            "No bearer-token-shaped parameters were found on IChatServiceClient. If the naming " +
            "convention changed, update BearerTokenParameterNames above rather than letting this " +
            "guard go silently inert.");
    }
}
