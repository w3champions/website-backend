using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using W3C.Domain.MatchmakingService;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.Sessions;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// POST api/maps/temporary: the answer for every outcome the service contract (Task 5a §9, corrected) lets escape, the
/// in-flight gate acquired before the first body byte and released after the last compensation step, and the fail-closed
/// 401. The bytes are always "abc"; matchmaking and update-service are scripted.
/// </summary>
[TestFixture]
public class TemporaryMapsControllerUploadTests : TemporaryMapUploadServiceTestBase
{
    /// <summary>ASP.NET Core's response serializer defaults (camelCase), which Program.cs leaves untouched.</summary>
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>Shared by every controller of a test, as the process-wide singleton is.</summary>
    private TemporaryMapUploadGate Gate { get; set; }

    [SetUp]
    public void NewGate() => Gate = new TemporaryMapUploadGate();

    // ---- Success --------------------------------------------------------------------------

    [Test]
    public async Task ANewMap_Is201_WithTheAppendixA3Body_AndNoSecret()
    {
        var handler = StoredNewMapHandler().On(IsCreate, Respond(HttpStatusCode.Created, Record(5811)));

        var result = await Upload(CreateController(handler)) as ObjectResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StatusCode, Is.EqualTo(StatusCodes.Status201Created));
        var body = JObject.Parse(JsonSerializer.Serialize(result.Value, WebJson));
        Assert.That(body.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "mapId", "path", "name", "sha1" }));
        Assert.That(body["mapId"]!.Value<int>(), Is.EqualTo(5811));
        Assert.That(body["path"]!.Value<string>(), Is.EqualTo(FileKey));
        Assert.That(body["name"]!.Value<string>(), Is.EqualTo("Legion TD"));
        Assert.That(body["sha1"]!.Value<string>(), Is.EqualTo(Sha1));
        Assert.That(body.ToString(), Does.Not.Contain(MapProofValue).And.Not.Contain(ProofHash));
        Assert.That(Gate.InFlight, Is.Zero, "the slot is released once the service returns");
    }

    [Test]
    public async Task ADedupeHit_Is200()
    {
        var handler = new ScriptedHttpHandler().On(IsBySha1, Respond(HttpStatusCode.OK, Record(5811)));

        var result = await Upload(CreateController(handler)) as ObjectResult;

        Assert.That(result?.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(((TemporaryMapUploadResponse)result!.Value!).MapId, Is.EqualTo(5811));
        Assert.That(Gate.InFlight, Is.Zero);
    }

    // ---- TemporaryMapUploadException: its status and body -----------------------------------

    [Test]
    public async Task AnUploadRejection_IsAnsweredWithItsStatusAndBody_AndCountedRejected()
    {
        var handler = new ScriptedHttpHandler();
        var counts = new UploadCounts();

        var result = await Upload(CreateController(handler), metadataSha1: OtherSha1) as ObjectResult;

        Assert.That(result?.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        var body = JObject.Parse(JsonSerializer.Serialize(result!.Value, WebJson));
        Assert.That(body["code"]!.Value<string>(), Is.EqualTo("SHA1_MISMATCH"));
        Assert.That(body.Properties().Count(), Is.EqualTo(1));
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Rejected);
        Assert.That(Gate.InFlight, Is.Zero);
    }

    [Test]
    public async Task AnUpstreamFailure_IsAnswered502Upstream_AndNeverEscapes_AndCountedUpstreamError()
    {
        var handler = StoredNewMapHandler()
            .On(IsCreate, TransportFails())
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var counts = new UploadCounts();

        var result = await Upload(CreateController(handler)) as ObjectResult;

        Assert.That(result?.StatusCode, Is.EqualTo(StatusCodes.Status502BadGateway));
        Assert.That(JObject.Parse(JsonSerializer.Serialize(result!.Value, WebJson))["code"]!.Value<string>(), Is.EqualTo("UPSTREAM"));
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.UpstreamError);
        Assert.That(Gate.InFlight, Is.Zero, "released only after the compensation, which ran inside the service");
    }

    [Test]
    public async Task TheUploadQuota_Is429_WithRetryAfterSeconds()
    {
        var limiter = new MintRateLimiter();
        for (var i = 0; i < TemporaryMapLimits.UploadsPerHourPerBattleTag; i++)
        {
            limiter.TryAcquire("tm-upload:" + BattleTag, TemporaryMapLimits.UploadsPerHourPerBattleTag, DateTime.UtcNow, TemporaryMapLimits.UploadQuotaWindow, out _);
        }

        var counts = new UploadCounts();

        var result = await Upload(CreateController(UnknownSha1Handler(), limiter)) as ObjectResult;

        Assert.That(result?.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
        var body = JObject.Parse(JsonSerializer.Serialize(result!.Value, WebJson));
        Assert.That(body.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "code", "retryAfterSeconds" }));
        Assert.That(body["code"]!.Value<string>(), Is.EqualTo("QUOTA_EXCEEDED"));
        Assert.That(body["retryAfterSeconds"]!.Value<int>(), Is.InRange(1, 3600));
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Rejected);
    }

    [Test]
    public async Task TheTwentyFirstAttemptInAnHour_Is429_QuotaExceeded_BeforeTheBodyIsRead()
    {
        // Every upload attempt spends an attempt token once it holds a slot, before the body is read: a slot cannot be
        // re-taken indefinitely for free, however the attempt ends (dedupe, rejection, an abandoned body). The record
        // quota (10/h, spent only for a new record) is a separate, tighter bound.
        var limiter = new MintRateLimiter();
        for (var i = 0; i < TemporaryMapLimits.UploadAttemptsPerHourPerBattleTag; i++)
        {
            Assert.That(limiter.TryAcquire("tm-upload-attempt:" + BattleTag, TemporaryMapLimits.UploadAttemptsPerHourPerBattleTag, DateTime.UtcNow,
                TemporaryMapLimits.UploadAttemptWindow, out _), Is.True);
        }

        var (bytes, contentType) = BuildMultipartBytes(Metadata(), "abc"u8.ToArray());
        var body = new MemoryStream(bytes);
        var handler = new ScriptedHttpHandler().On(IsBySha1, Respond(HttpStatusCode.OK, Record(5811)));
        var counts = new UploadCounts();
        using var logs = new LogCapture();

        var result = await Upload(CreateController(handler, limiter, logs.CreateLogger<TemporaryMapsController>()), body: body, contentType: contentType) as ObjectResult;

        Assert.That(result?.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
        var answer = JObject.Parse(JsonSerializer.Serialize(result!.Value, WebJson));
        Assert.That(answer.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "code", "retryAfterSeconds" }), "the pinned A.3 body, no new wire names");
        Assert.That(answer["code"]!.Value<string>(), Is.EqualTo("QUOTA_EXCEEDED"));
        Assert.That(answer["retryAfterSeconds"]!.Value<int>(), Is.InRange(1, 3600), "the window's remaining seconds, rounded up");
        Assert.That(body.Position, Is.Zero, "refused before the first body byte");
        Assert.That(handler.Requests, Is.Empty, "no upstream was asked");
        Assert.That(Gate.InFlight, Is.Zero, "the slot taken for the attempt was handed back");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Rejected);
        Assert.That(logs.Lines().Single(l => l.Contains("attempt quota", StringComparison.Ordinal)), Does.StartWith("Information").And.Contain(BattleTag));
        Assert.That(TemporaryMapLimits.UploadAttemptsPerHourPerBattleTag, Is.EqualTo(20));
        Assert.That(TemporaryMapLimits.UploadAttemptWindow, Is.EqualTo(TimeSpan.FromHours(1)));
    }

    [Test]
    public async Task ADedupeHit_SpendsAnAttemptToken_ButNoRecordToken()
    {
        var limiter = new MintRateLimiter();
        var handler = new ScriptedHttpHandler().On(IsBySha1, Respond(HttpStatusCode.OK, Record(5811)));

        var result = await Upload(CreateController(handler, limiter)) as ObjectResult;

        Assert.That(result?.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        Assert.That(Remaining("tm-upload-attempt:" + BattleTag, TemporaryMapLimits.UploadAttemptsPerHourPerBattleTag, TemporaryMapLimits.UploadAttemptWindow),
            Is.EqualTo(TemporaryMapLimits.UploadAttemptsPerHourPerBattleTag - 1), "one attempt token was spent");
        Assert.That(Remaining("tm-upload:" + BattleTag, TemporaryMapLimits.UploadsPerHourPerBattleTag, TemporaryMapLimits.UploadQuotaWindow),
            Is.EqualTo(TemporaryMapLimits.UploadsPerHourPerBattleTag), "a dedupe hit creates no record, so the record quota is untouched");

        int Remaining(string key, int limit, TimeSpan window)
        {
            var left = 0;
            while (limiter.TryAcquire(key, limit, DateTime.UtcNow, window, out _))
            {
                left++;
            }

            return left;
        }
    }

    [Test]
    public async Task AGateRefusal_SpendsNoAttemptToken()
    {
        // The attempt token is spent only by an attempt that holds a slot: a refusal at the gate never reached the body
        // and is already answered 429.
        var limiter = new MintRateLimiter();
        Assert.That(Gate.TryAcquire(BattleTag, out var held, out _), Is.True);
        using (held)
        {
            var refused = await Upload(CreateController(new ScriptedHttpHandler(), limiter)) as ObjectResult;
            Assert.That(refused?.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
        }

        Assert.That(limiter.TryAcquire("tm-upload-attempt:" + BattleTag, 1, DateTime.UtcNow, TemporaryMapLimits.UploadAttemptWindow, out _), Is.True,
            "the attempt window was never opened");
    }

    // ---- Server faults and transport errors ------------------------------------------------

    [Test]
    public async Task ASpoolFault_IsABare500_LoggedOnceByTheReaderAndNotAgainByTheController()
    {
        Directory.CreateDirectory(TestRoot);
        var blocker = Path.Combine(TestRoot, "not-a-directory");
        File.WriteAllText(blocker, "");
        using var logs = new LogCapture();
        var controller = CreateController(new ScriptedHttpHandler(), logger: logs.CreateLogger<TemporaryMapsController>(),
            spoolDirectory: Path.Combine(blocker, "spool"));
        var counts = new UploadCounts();

        var result = await Upload(controller);

        AssertBareStatus(result, StatusCodes.Status500InternalServerError);
        Assert.That(logs.Lines().Where(l => l.StartsWith("Error", StringComparison.Ordinal)), Has.Exactly(1).Items, "the reader's own log line");
        Assert.That(logs.Lines().Where(l => l.Contains(nameof(TemporaryMapsController), StringComparison.Ordinal)), Is.Empty,
            "the controller adds no second line");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.ServerError);
        Assert.That(Gate.InFlight, Is.Zero);
    }

    [Test]
    public async Task AClientAbortWhileTheBodyIsRead_IsAnEmptyResult_AndIsNotCounted()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        using var logs = new LogCapture();
        var controller = CreateController(UnknownSha1Handler(), logger: logs.CreateLogger<TemporaryMapsController>());
        controller.HttpContext.RequestAborted = aborted.Token;
        var counts = new UploadCounts();

        var result = await Upload(controller, cancellationToken: aborted.Token);

        Assert.That(result, Is.InstanceOf<EmptyResult>(), "nobody is listening");
        Assert.That(logs.Lines().Where(l => l.StartsWith("Warning", StringComparison.Ordinal) || l.StartsWith("Error", StringComparison.Ordinal)), Is.Empty);
        counts.AssertNothingCounted();
        Assert.That(Gate.InFlight, Is.Zero);
    }

    [TestCase("the reader's own rejection", TestName = "WhateverEscapesAfterTheClientAborted_IsAnEmptyResult(TemporaryMapUploadException)")]
    [TestCase("a spool fault", TestName = "WhateverEscapesAfterTheClientAborted_IsAnEmptyResult(TemporaryMapSpoolException)")]
    [TestCase("a stream fault", TestName = "WhateverEscapesAfterTheClientAborted_IsAnEmptyResult(InvalidOperationException)")]
    [TestCase("connection reset", TestName = "ABodyErrorAfterTheClientAborted_IsAnEmptyResult(IOException)")]
    [TestCase("kestrel 413", TestName = "ABodyErrorAfterTheClientAborted_IsAnEmptyResult(BadHttpRequestException 413)")]
    [TestCase("kestrel 400", TestName = "ABodyErrorAfterTheClientAborted_IsAnEmptyResult(BadHttpRequestException 400)")]
    public async Task ABodyErrorAfterTheClientAborted_IsAnEmptyResult(string failure)
    {
        // Kestrel cancels RequestAborted before the read fails; the abort is checked before any status is chosen, and
        // before any exception type is looked at (Task 5a §9, item 1): whatever escaped, nobody is listening.
        using var aborted = new CancellationTokenSource();
        var body = new ThrowingStream(failure switch
        {
            "the reader's own rejection" => new TemporaryMapUploadException(StatusCodes.Status400BadRequest, "METADATA"),
            "a spool fault" => new TemporaryMapSpoolException("The spool file could not be written.", new IOException("disk full")),
            "a stream fault" => new InvalidOperationException("simulated stream fault"),
            "connection reset" => new IOException("simulated connection reset"),
            "kestrel 413" => new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge),
            _ => new BadHttpRequestException("Unexpected end of request content.", StatusCodes.Status400BadRequest),
        }, abortFirst: aborted);
        using var logs = new LogCapture();
        var controller = CreateController(UnknownSha1Handler(), logger: logs.CreateLogger<TemporaryMapsController>());
        controller.HttpContext.RequestAborted = aborted.Token;

        var result = await Upload(controller, body: body, cancellationToken: aborted.Token);

        Assert.That(result, Is.InstanceOf<EmptyResult>());
        Assert.That(logs.Lines().Where(l => l.StartsWith("Warning", StringComparison.Ordinal) || l.StartsWith("Error", StringComparison.Ordinal)), Is.Empty,
            "a client that went away is not a fault of this service");
        Assert.That(logs.Lines().Where(l => l.Contains("abandoned by the client", StringComparison.Ordinal)), Has.Exactly(1).Items);
        Assert.That(Gate.InFlight, Is.Zero);
    }

    [Test]
    public async Task AnUpstreamFailureAfterTheClientLeft_IsAnEmptyResult_ButCountedByItsOutcome()
    {
        // The bytes are stored and the client goes away while the record write fails: the service runs on without the
        // request token, compensates, maps the failure to 502 UPSTREAM and counts it — the upload did reach a terminal
        // outcome — but nobody is listening for the answer.
        using var aborted = new CancellationTokenSource();
        var handler = StoredNewMapHandler()
            .On(IsCreate, _ =>
            {
                aborted.Cancel();
                throw new HttpRequestException("simulated transport failure");
            })
            .On(IsUsDelete, Respond(HttpStatusCode.NoContent, ""));
        var controller = CreateController(handler);
        controller.HttpContext.RequestAborted = aborted.Token;
        var counts = new UploadCounts();

        var result = await Upload(controller, cancellationToken: aborted.Token);

        Assert.That(result, Is.InstanceOf<EmptyResult>());
        Assert.That(Count(handler, IsUsDelete), Is.EqualTo(1), "compensation ran to the end");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.UpstreamError);
        Assert.That(Gate.InFlight, Is.Zero, "released only after the compensation");
    }

    [Test]
    public async Task KestrelsBodyLimit_Is413_FileTooLarge_AndCountedRejected()
    {
        var body = new ThrowingStream(new BadHttpRequestException("Request body too large. The max request body size is 269484032 bytes.",
            StatusCodes.Status413PayloadTooLarge));
        var counts = new UploadCounts();

        var result = await Upload(CreateController(UnknownSha1Handler()), body: body) as ObjectResult;

        Assert.That(result?.StatusCode, Is.EqualTo(StatusCodes.Status413PayloadTooLarge));
        Assert.That(JObject.Parse(JsonSerializer.Serialize(result!.Value, WebJson))["code"]!.Value<string>(), Is.EqualTo("FILE_TOO_LARGE"));
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Rejected);
        Assert.That(Gate.InFlight, Is.Zero);
    }

    [TestCase(StatusCodes.Status400BadRequest)]
    [TestCase(StatusCodes.Status408RequestTimeout)]
    [TestCase(StatusCodes.Status431RequestHeaderFieldsTooLarge)]
    public async Task AnyOtherKestrelRequestError_Is400_Metadata_AndCountedRejected(int kestrelStatus)
    {
        var body = new ThrowingStream(new BadHttpRequestException("Bad request.", kestrelStatus));
        var counts = new UploadCounts();

        var result = await Upload(CreateController(UnknownSha1Handler()), body: body) as ObjectResult;

        Assert.That(result?.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(JObject.Parse(JsonSerializer.Serialize(result!.Value, WebJson))["code"]!.Value<string>(), Is.EqualTo("METADATA"));
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Rejected);
    }

    [TestCase("truncated multipart")]
    [TestCase("connection reset")]
    public async Task ABodyThatCannotBeRead_Is400_Metadata(string failure)
    {
        var (bytes, contentType) = BuildMultipartBytes(Metadata(), "abc"u8.ToArray());
        Stream body = failure == "truncated multipart"
            ? new MemoryStream(bytes, 0, bytes.Length - 20)
            : new ThrowingStream(new IOException("simulated connection reset"));
        var counts = new UploadCounts();

        var result = await Upload(CreateController(UnknownSha1Handler()), body: body, contentType: contentType) as ObjectResult;

        Assert.That(result?.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(JObject.Parse(JsonSerializer.Serialize(result!.Value, WebJson))["code"]!.Value<string>(), Is.EqualTo("METADATA"));
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Rejected);
        Assert.That(Gate.InFlight, Is.Zero);
    }

    [Test]
    public async Task ACancellationTheClientDidNotCause_IsABare500_AndLogsAtError()
    {
        // Cannot happen for the request token (the service maps an HttpClient timeout to 502 UPSTREAM, and MVC binds the
        // action's token to RequestAborted); if it ever does, it is a server fault, not a client that went away. The
        // service, which knows only the token it was given, has treated it as an abort and counted nothing.
        using var notTheRequest = new CancellationTokenSource();
        notTheRequest.Cancel();
        using var logs = new LogCapture();
        var controller = CreateController(UnknownSha1Handler(), logger: logs.CreateLogger<TemporaryMapsController>());
        var counts = new UploadCounts();

        var result = await Upload(controller, cancellationToken: notTheRequest.Token);

        AssertBareStatus(result, StatusCodes.Status500InternalServerError);
        Assert.That(logs.Lines().Where(l => l.StartsWith("Error", StringComparison.Ordinal)), Has.Exactly(1).Items);
        counts.AssertNothingCounted();
        Assert.That(Gate.InFlight, Is.Zero);
    }

    [Test]
    public async Task AnUnexpectedFailure_IsABare500_AndLogsItAtError_AndCountedServerError()
    {
        using var logs = new LogCapture();
        var controller = CreateController(UnknownSha1Handler(), logger: logs.CreateLogger<TemporaryMapsController>());
        var counts = new UploadCounts();

        var result = await Upload(controller, body: new ThrowingStream(new InvalidOperationException("simulated stream fault")));

        AssertBareStatus(result, StatusCodes.Status500InternalServerError);
        var errors = logs.Lines().Where(l => l.StartsWith("Error", StringComparison.Ordinal)).ToArray();
        Assert.That(errors, Has.Length.EqualTo(1));
        Assert.That(errors[0], Does.Contain("InvalidOperationException").And.Contain("simulated stream fault"), "the exception itself is logged: nothing else knows it");
        // The label follows the 500 this route answers, not an upstream that was never involved.
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.ServerError);
        Assert.That(Gate.InFlight, Is.Zero);
    }

    // ---- Fail closed ------------------------------------------------------------------------

    [TestCase(null)]
    [TestCase("")]
    [TestCase("missing")]
    public async Task WithoutABattleTag_Is401_BeforeTheBodyIsTouched(string battleTag)
    {
        var (bytes, contentType) = BuildMultipartBytes(Metadata(), "abc"u8.ToArray());
        var body = new MemoryStream(bytes);
        var handler = new ScriptedHttpHandler().On(IsBySha1, Respond(HttpStatusCode.OK, Record(5811)));
        var controller = CreateController(handler, battleTag: battleTag == "missing" ? null : battleTag);
        if (battleTag == "missing")
        {
            controller.HttpContext.Items.Remove(BearerRequiresPlayerAuthFilter.BattleTagItemKey);
        }

        var result = await Upload(controller, body: body, contentType: contentType);

        AssertBareStatus(result, StatusCodes.Status401Unauthorized);
        Assert.That(body.Position, Is.Zero, "not a byte of the body is read");
        Assert.That(handler.Requests, Is.Empty);
    }

    // ---- The in-flight gate -----------------------------------------------------------------

    [Test]
    public async Task ASecondUploadOfTheSameBattleTag_Is429_WhileTheFirstIsInFlight_AndReadsNoBody()
    {
        var firstIsStoring = NewSignal();
        var releaseTheStore = NewSignal();
        var handler = UnknownSha1Handler()
            .On(IsUsUpload, _ =>
            {
                firstIsStoring.TrySetResult();
                Assert.That(releaseTheStore.Task.Wait(HangGuard), Is.True, "the test never released the store");
                return ScriptedHttpHandler.Json(HttpStatusCode.OK, UsUploadBody());
            })
            .On(IsCreate, Respond(HttpStatusCode.Created, Record(5811)));
        var (bytes, contentType) = BuildMultipartBytes(Metadata(), "abc"u8.ToArray());
        var secondBody = new MemoryStream(bytes);
        var counts = new UploadCounts();
        using var logs = new LogCapture();

        var first = Task.Run(() => Upload(CreateController(handler)));
        IActionResult second;
        try
        {
            await firstIsStoring.Task.WaitAsync(HangGuard);
            second = await Upload(CreateController(handler, logger: logs.CreateLogger<TemporaryMapsController>()), body: secondBody, contentType: contentType);
        }
        finally
        {
            releaseTheStore.TrySetResult();
        }

        var refused = second as ObjectResult;
        Assert.That(refused?.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
        var body = JObject.Parse(JsonSerializer.Serialize(refused!.Value, WebJson));
        Assert.That(body.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "code", "retryAfterSeconds" }), "the pinned A.3 body, no new wire names");
        Assert.That(body["code"]!.Value<string>(), Is.EqualTo("QUOTA_EXCEEDED"));
        Assert.That(body["retryAfterSeconds"]!.Value<int>(), Is.EqualTo(TemporaryMapLimits.ConcurrentUploadRetryAfterSeconds));
        Assert.That(secondBody.Position, Is.Zero, "the gate is consulted before the first body byte");
        Assert.That(((ObjectResult)await first.WaitAsync(HangGuard)).StatusCode, Is.EqualTo(StatusCodes.Status201Created), "the first upload is unaffected");
        Assert.That(handler.Requests.Select(Route), Is.EqualTo(new[] { "by-sha1", "us-upload", "create" }), "the refused upload reached no upstream");
        Assert.That(Gate.InFlight, Is.Zero, "the first upload released its slot");
        counts.AssertCountedOnceAs(TemporaryMapMetrics.Results.Rejected, TemporaryMapMetrics.Results.Created);
        var refusal = logs.Lines().Single(l => l.Contains("refused", StringComparison.Ordinal));
        Assert.That(refusal, Does.StartWith("Information").And.Contain("Bound=PerBattleTag").And.Contain("InFlight=1"), "which bound refused, and how full the gate was");
    }

    [Test]
    public async Task TheNinthConcurrentUpload_Is429_UntilASlotIsFreed()
    {
        var others = Enumerable.Range(0, TemporaryMapLimits.MaxConcurrentUploads).Select(i =>
        {
            Assert.That(Gate.TryAcquire($"player{i}#1", out var slot, out _), Is.True);
            return slot;
        }).ToList();
        var handler = StoredNewMapHandler().On(IsCreate, Respond(HttpStatusCode.Created, Record(5811)));
        var (bytes, contentType) = BuildMultipartBytes(Metadata(), "abc"u8.ToArray());
        var body = new MemoryStream(bytes);
        using var logs = new LogCapture();

        var refused = await Upload(CreateController(handler, logger: logs.CreateLogger<TemporaryMapsController>()), body: body, contentType: contentType) as ObjectResult;

        Assert.That(refused?.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
        Assert.That(JObject.Parse(JsonSerializer.Serialize(refused!.Value, WebJson))["retryAfterSeconds"]!.Value<int>(), Is.EqualTo(30));
        Assert.That(body.Position, Is.Zero);
        Assert.That(handler.Requests, Is.Empty);
        Assert.That(Gate.InFlight, Is.EqualTo(TemporaryMapLimits.MaxConcurrentUploads), "the refusal took no slot from anyone");
        var refusal = logs.Lines().Single(l => l.Contains("refused", StringComparison.Ordinal));
        Assert.That(refusal, Does.Contain("Bound=Global").And.Contain("InFlight=8").And.Contain("MaxConcurrentUploads=8"));
        Assert.That(refusal, Does.Contain(BattleTag).And.Not.Contain("player"), "the refused player is named; the players holding the slots never are");

        others[0].Dispose();

        var admitted = await Upload(CreateController(handler), body: body, contentType: contentType) as ObjectResult;
        Assert.That(admitted?.StatusCode, Is.EqualTo(StatusCodes.Status201Created));
        others.ForEach(s => s.Dispose());
        Assert.That(Gate.InFlight, Is.Zero);
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private static void AssertBareStatus(IActionResult result, int status)
    {
        // StatusCodeResult and its subclasses write a status only; an ObjectResult is not one of them.
        Assert.That(result, Is.InstanceOf<StatusCodeResult>(), "a bare status, no body");
        Assert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(status));
    }

    private TemporaryMapsController CreateController(
        ScriptedHttpHandler handler,
        MintRateLimiter limiter = null,
        ILogger<TemporaryMapsController> logger = null,
        string battleTag = BattleTag,
        string spoolDirectory = null)
    {
        var factory = new ScriptedHttpHandler.Factory(handler);
        var matchmaking = new MatchmakingServiceClient(factory);
        var rateLimiter = limiter ?? new MintRateLimiter();
        var service = new TemporaryMapUploadService(matchmaking, new UpdateServiceClient(factory), rateLimiter, FileKeyLock, NullLogger<TemporaryMapUploadService>.Instance)
        {
            SpoolDirectory = spoolDirectory ?? SpoolDirectory,
            WaitAsync = _ => Task.CompletedTask,
        };
        var controller = new TemporaryMapsController(service, matchmaking, rateLimiter, Gate, logger ?? NullLogger<TemporaryMapsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.HttpContext.Items[BearerRequiresPlayerAuthFilter.BattleTagItemKey] = battleTag;
        return controller;
    }

    /// <summary>One POST of the bytes "abc" (or <paramref name="body"/>) through <paramref name="controller"/>.</summary>
    private static Task<IActionResult> Upload(
        TemporaryMapsController controller,
        Stream body = null,
        string contentType = null,
        string metadataSha1 = Sha1,
        CancellationToken cancellationToken = default)
    {
        var (defaultBody, defaultContentType) = BuildMultipart(Metadata(metadataSha1), "abc"u8.ToArray());
        controller.Request.Body = body ?? defaultBody;
        controller.Request.ContentType = contentType ?? defaultContentType;
        return controller.Upload(cancellationToken);
    }

    /// <summary>A request body whose first read fails like Kestrel's would, after optionally aborting the request.</summary>
    private sealed class ThrowingStream(Exception failure, CancellationTokenSource abortFirst = null) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => Fail();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(Fail());

        private int Fail()
        {
            abortFirst?.Cancel();
            throw failure;
        }
    }
}
