using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Serilog.Events;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.Maps;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// Design spec §10.3: the upload orchestration never logs a mapProof, a proofHash or an update-service MapProofHash
/// (sha1 may be logged), and never takes one as a parameter TracingInterceptor would record.
/// </summary>
[TestFixture]
public class TemporaryMapUploadServiceSecrecyTests : TemporaryMapUploadServiceTestBase
{
    private static readonly HashSet<string> SecretParameterNames = new(StringComparer.OrdinalIgnoreCase) { "proofHash", "mapProof" };

    [Test]
    public async Task NoLogEvent_CarriesTheProofOrItsHash_AcrossSuccessAndFailureFlows()
    {
        using var logs = new LogCapture();

        // A new map is created.
        await Run(StoredNewMapHandler().On(IsCreate, Respond(HttpStatusCode.Created, Record(5811))), logger: logs.Logger);

        // A restore whose file-restored call goes unanswered, whose re-probe says deleted, and whose compensation fails.
        var restore = OnSequence(new ScriptedHttpHandler(), IsBySha1,
                Respond(HttpStatusCode.OK, Record(5811, fileState: "deleted")),
                Respond(HttpStatusCode.OK, Record(5811, fileState: "deleted")))
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(5811)))
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()))
            .On(IsFileRestored, TimesOut())
            .On(IsUsDelete, Respond(HttpStatusCode.ServiceUnavailable, ""));
        await ExpectFailure(Run(restore, withCapture: false, logger: logs.Logger));

        // verify-proof names another record, and one whose stored path is not a temporary file.
        await ExpectFailure(Run(DeletedRecordHandler().On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(42))), logger: logs.Logger));
        await ExpectFailure(Run(DeletedRecordHandler().On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(5811, "W3Champions/v10/x.w3x"))),
            logger: logs.Logger));

        // update-service derives a different proof hash (the hash it returned must not be logged either).
        await ExpectFailure(Run(UnknownSha1Handler()
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody(mapProofHash: ProofHash.ToUpperInvariant())))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, "")), logger: logs.Logger));

        // The path-conflict guard refuses a claimed path, and matchmaking refuses and then fails to answer a create.
        await ExpectFailure(Run(UnknownSha1Handler()
            .On(IsUsUpload, Respond(HttpStatusCode.Conflict, "{}"))
            .On(IsByPath, Respond(HttpStatusCode.OK, Record(4242, sha1: OtherSha1))), logger: logs.Logger));
        await ExpectFailure(Run(StoredNewMapHandler()
            .On(IsCreate, Respond(HttpStatusCode.BadRequest, "{\"errors\":[{\"param\":\"gameMap\",\"message\":\"boom\"}]}"))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, "")), logger: logs.Logger));
        await ExpectFailure(Run(OnSequence(new ScriptedHttpHandler(), IsBySha1, Respond(HttpStatusCode.NotFound), TimesOut())
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()))
            .On(IsCreate, TransportFails()), logger: logs.Logger));

        var lines = logs.Lines();
        Assert.That(logs.Sink.Events.Select(e => e.Level).Distinct(),
            Is.SupersetOf(new[] { LogEventLevel.Information, LogEventLevel.Warning, LogEventLevel.Error }),
            "the flows must actually log, or this check is vacuous");
        Assert.That(lines, Has.Some.Contains(Sha1), "sha1 may be logged, and is");
        AssertNoSecretIn(lines);
    }

    [Test]
    public async Task MatchmakingErrorText_EchoingTheProof_NeverReachesTheLogs_FromAProofCarryingCall()
    {
        // matchmaking's generic 400 echoes raw exception text, which can quote the request it refused.
        using var logs = new LogCapture();
        var echo = "{\"errors\":[{\"param\":\"mapProof\",\"msg\":\"refused " + MapProofValue + " hashing to " + ProofHash + "\"}]}";

        await ExpectFailure(Run(StoredNewMapHandler()
            .On(IsCreate, Respond(HttpStatusCode.BadRequest, echo))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, "")), logger: logs.Logger));
        await ExpectFailure(Run(DeletedRecordHandler()
            .On(IsVerifyProof, Respond(HttpStatusCode.OK, Verified(5811)))
            .On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()))
            .On(IsFileRestored, Respond(HttpStatusCode.BadRequest, echo))
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, "")), withCapture: false, logger: logs.Logger));
        await ExpectFailure(Run(DeletedRecordHandler().On(IsVerifyProof, Respond(HttpStatusCode.InternalServerError, echo)), logger: logs.Logger));

        var failures = logs.Lines().Where(l => l.StartsWith("Warning") && l.Contains("HttpRequestException")).ToArray();
        Assert.That(failures, Has.Length.EqualTo(3), "each proof-carrying call names the exception type");
        Assert.That(failures.Count(l => l.Contains("400")), Is.EqualTo(2), "and the upstream status");
        Assert.That(failures.Count(l => l.Contains("500")), Is.EqualTo(1));
        Assert.That(logs.Lines(), Has.None.Contains("refused"), "matchmaking's error text is not logged at all");
        AssertNoSecretIn(logs.Lines());
    }

    private static void AssertNoSecretIn(string[] lines)
    {
        foreach (var secret in new[] { MapProofValue, ProofHash, ProofHash.ToUpperInvariant(), MapProofValue.ToUpperInvariant() })
        {
            Assert.That(lines, Has.None.Contains(secret));
        }
    }

    [Test]
    public void NoPublicMethod_TakesAProofWithoutNoTrace()
    {
        var parameters = typeof(TemporaryMapUploadService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetParameters())
            .ToList();

        Assert.That(parameters, Is.Not.Empty);
        Assert.That(parameters.Where(p => CarriesASecret(p) && p.GetCustomAttribute<NoTraceAttribute>() == null).Select(p => p.Name),
            Is.Empty, "a proof-carrying parameter would become a param.{name} activity tag once the service is intercepted");
    }

    private static bool CarriesASecret(ParameterInfo parameter)
        => SecretParameterNames.Contains(parameter.Name!)
           || parameter.ParameterType.GetProperties().Any(p => SecretParameterNames.Contains(p.Name));

    private static async Task ExpectFailure(Task<TemporaryMapUploadOutcome> upload)
    {
        try
        {
            await upload;
            Assert.Fail("the scripted flow was expected to fail");
        }
        catch (TemporaryMapUploadException)
        {
            // Expected: only the log events matter here.
        }
    }
}
