using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using NUnit.Framework;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.Services;
using WC3ChampionsStatisticService.Tests.Maps;

namespace WC3ChampionsStatisticService.Tests.Services;

/// <summary>
/// ReplayServiceClient sends the admin secret as <c>?secret=</c> and IdentityServiceClient the caller's JWT as
/// <c>?authorization=</c>. Each value is escaped exactly once, so a character that means something in a URL can
/// neither end the value early nor leave part of it outside the query, where telemetry redaction would not see it.
/// Placeholders only; never a real credential.
/// </summary>
[TestFixture]
public class CredentialQueryEscapingTests
{
    /// <summary>Every character that ends or changes a query value, plus an escape sequence that must survive as text.</summary>
    private const string HostileValue = "placeholder&credential=x#part+under test%2F";

    /// <summary>The shape of today's values (GUID secret, base64url JWT): escaping must leave them unchanged.</summary>
    private const string GuidShaped = "00000000-0000-4000-8000-000000000000";
    private const string JwtShaped = "header-part.payload_part.signature-part";

    private static readonly Dictionary<string, (HttpMethod Method, Func<IdentityServiceClient, string, Task> Call)> IdentityCalls = new()
    {
        ["GetPermissions"] = (HttpMethod.Get, (c, authorization) => c.GetPermissions(authorization)),
        ["AddAdmin"] = (HttpMethod.Post, (c, authorization) => c.AddAdmin(new Permission(), authorization)),
        ["EditAdmin"] = (HttpMethod.Put, (c, authorization) => c.EditAdmin(new Permission(), authorization)),
        ["DeleteAdmin"] = (HttpMethod.Delete, (c, authorization) => c.DeleteAdmin("Peter#123", authorization)),
    };

    private static IEnumerable<string> IdentityCallNames() => IdentityCalls.Keys;

    [TestCaseSource(nameof(IdentityCallNames))]
    public async Task IdentityServiceClient_EscapesTheAuthorizationValueExactlyOnce(string callName)
    {
        var (method, call) = IdentityCalls[callName];
        var handler = AnswersEverything();

        await call(new IdentityServiceClient(new HttpClient(handler)), HostileValue);

        var request = handler.Requests.Single();
        Assert.That(request.Method, Is.EqualTo(method));
        Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo("/api/permissions"));
        Assert.That(request.RequestUri.Fragment, Is.Empty);
        var query = DecodedQuery(request);
        Assert.That(query["authorization"], Is.EqualTo(HostileValue));
        Assert.That(query.Keys, Is.EquivalentTo(callName == "DeleteAdmin" ? new[] { "id", "authorization" } : new[] { "authorization" }));
    }

    [TestCaseSource(nameof(IdentityCallNames))]
    public async Task IdentityServiceClient_LeavesAJwtShapedValueUnchanged(string callName)
    {
        var handler = AnswersEverything();

        await IdentityCalls[callName].Call(new IdentityServiceClient(new HttpClient(handler)), JwtShaped);

        var expectedQuery = callName == "DeleteAdmin" ? $"?id=Peter%23123&authorization={JwtShaped}" : $"?authorization={JwtShaped}";
        Assert.That(handler.Requests.Single().RequestUri!.Query, Is.EqualTo(expectedQuery));
    }

    [TestCase("generate/42")]
    [TestCase("chats/42")]
    public void ReplayServiceClient_EscapesTheSecretValueExactlyOnce(string path)
    {
        var uri = new Uri(ReplayServiceClient.SecretUrl(path, HostileValue));

        Assert.That(uri.AbsolutePath, Is.EqualTo("/" + path));
        Assert.That(uri.Fragment, Is.Empty);
        Assert.That(QueryHelpers.ParseQuery(uri.Query).ToDictionary(p => p.Key, p => p.Value.ToString()),
            Is.EqualTo(new Dictionary<string, string> { ["secret"] = HostileValue }));
    }

    [Test]
    public void ReplayServiceClient_LeavesAGuidShapedSecretUnchanged()
    {
        Assert.That(ReplayServiceClient.SecretUrl("chats/42", GuidShaped), Does.EndWith($"/chats/42?secret={GuidShaped}"));
    }

    [Test]
    public async Task ReplayServiceClient_SendsTheConfiguredSecretOnceOnBothRoutes()
    {
        var handler = new ScriptedHttpHandler()
            .On(HttpMethod.Get, "/generate/42", HttpStatusCode.OK, "replay")
            .On(HttpMethod.Get, "/chats/42", HttpStatusCode.OK, "{}");
        var client = new ReplayServiceClient(new ScriptedHttpHandler.Factory(handler));

        await using var replay = await client.GenerateReplay(42);
        await client.GetChatLogs(42);

        Assert.That(handler.Requests.Select(r => r.RequestUri!.AbsolutePath), Is.EqualTo(new[] { "/generate/42", "/chats/42" }));
        var configuredSecret = (string)typeof(ReplayServiceClient)
            .GetField("AdminSecret", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        foreach (var request in handler.Requests)
        {
            var query = DecodedQuery(request);
            Assert.That(query.Keys, Is.EqualTo(new[] { "secret" }));
            // Compared as a boolean so a failure never prints the secret.
            Assert.That(query["secret"] == configuredSecret, Is.True, "the replay routes must carry exactly the configured secret");
        }
    }

    private static ScriptedHttpHandler AnswersEverything()
        => new ScriptedHttpHandler().On(_ => true, _ => ScriptedHttpHandler.Json(HttpStatusCode.OK, "[]"));

    private static Dictionary<string, string> DecodedQuery(HttpRequestMessage request)
        => QueryHelpers.ParseQuery(request.RequestUri!.Query).ToDictionary(p => p.Key, p => p.Value.ToString());
}
