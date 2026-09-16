using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Serilog.Extensions.Logging;
using W3C.Domain.MatchmakingService;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.Extensions;
using W3ChampionsStatisticService.Maps;
using W3ChampionsStatisticService.Services.Interceptors;
using W3ChampionsStatisticService.Sessions;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace WC3ChampionsStatisticService.Tests.Maps;

/// <summary>
/// Shared by the <see cref="TemporaryMapUploadService"/> fixtures. Every upload carries the bytes "abc", so sha1,
/// mapProof and proofHash are the pinned §10.2 vectors. The real matchmaking and update-service clients run over one
/// <see cref="ScriptedHttpHandler"/>, and every test spools into its own directory (inherited from
/// <see cref="TemporaryMapUploadReaderTestBase"/>), never the machine-wide TempUploadDir.
/// </summary>
public abstract class TemporaryMapUploadServiceTestBase : TemporaryMapUploadReaderTestBase
{
    protected const string Sha1 = AbcSha1;
    protected const string MapProofValue = AbcMapProof;
    protected const string ProofHash = "6e60c9a01cfd09cec0b8493fe1ad9f2411ed21a1e1e58aab9eceeb3e9bac2bd6";
    protected const string FileKey = "W3Champions/CustomGames/Legion TD-a9993e36.w3x";
    protected const string UsFileName = "CustomGames/Legion TD-a9993e36.w3x";
    protected const string BattleTag = "peter#123";
    protected const string OtherBattleTag = "Other#5678";
    protected const string OtherSha1 = "0123456789abcdef0123456789abcdef01234567";

    private static readonly string[] AllResults =
    [
        TemporaryMapMetrics.Results.Created,
        TemporaryMapMetrics.Results.Deduped,
        TemporaryMapMetrics.Results.Restored,
        TemporaryMapMetrics.Results.Rejected,
        TemporaryMapMetrics.Results.UpstreamError,
        TemporaryMapMetrics.Results.ServerError,
    ];

    // ---- Scripted routes ------------------------------------------------------------------

    protected static bool IsBySha1(HttpRequestMessage r)
        => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.Contains("/maps/temporary/by-sha1/", StringComparison.Ordinal);

    protected static bool IsByPath(HttpRequestMessage r)
        => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/maps/temporary/by-path", StringComparison.Ordinal);

    /// <summary>POST /maps/temporary exactly: a substring match would also catch verify-proof and file-restored.</summary>
    protected static bool IsCreate(HttpRequestMessage r)
        => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/maps/temporary", StringComparison.Ordinal);

    protected static bool IsVerifyProof(HttpRequestMessage r)
        => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/maps/temporary/verify-proof", StringComparison.Ordinal);

    protected static bool IsFileRestored(HttpRequestMessage r)
        => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/file-restored", StringComparison.Ordinal);

    protected static bool IsUsUpload(HttpRequestMessage r)
        => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/api/content/maps", StringComparison.Ordinal);

    protected static bool IsUsDelete(HttpRequestMessage r)
        => r.Method == HttpMethod.Delete && r.RequestUri!.AbsolutePath.EndsWith("/api/content/maps/file", StringComparison.Ordinal);

    /// <summary>A short name for a scripted route, so a test can assert the exact call sequence.</summary>
    protected static string Route(HttpRequestMessage r)
        => IsBySha1(r) ? "by-sha1"
            : IsByPath(r) ? "by-path"
            : IsVerifyProof(r) ? "verify-proof"
            : IsFileRestored(r) ? "file-restored"
            : IsCreate(r) ? "create"
            : IsUsUpload(r) ? "us-upload"
            : IsUsDelete(r) ? "us-delete"
            : r.Method + " " + r.RequestUri!.AbsolutePath;

    private protected static int Count(ScriptedHttpHandler handler, Func<HttpRequestMessage, bool> route)
        => handler.Requests.Count(route);

    /// <summary>Scripts consecutive responders for one route; the last one repeats.</summary>
    private protected static ScriptedHttpHandler OnSequence(
        ScriptedHttpHandler handler, Func<HttpRequestMessage, bool> route, params Func<HttpRequestMessage, HttpResponseMessage>[] responders)
    {
        var index = 0;
        return handler.On(route, r => responders[Math.Min(index++, responders.Length - 1)](r));
    }

    protected static Func<HttpRequestMessage, HttpResponseMessage> Respond(HttpStatusCode status, string body = "{}")
        => _ => ScriptedHttpHandler.Json(status, body);

    /// <summary>An HttpClient timeout: the handler cancels although the caller's token did not.</summary>
    protected static Func<HttpRequestMessage, HttpResponseMessage> TimesOut()
        => _ => throw new TaskCanceledException("simulated HttpClient timeout");

    /// <summary>A transport failure: no HTTP status at all (connection refused, reset, DNS).</summary>
    protected static Func<HttpRequestMessage, HttpResponseMessage> TransportFails()
        => _ => throw new HttpRequestException("simulated connection refused");

    // ---- Bodies ------------------------------------------------------------------------------

    protected static string Record(int id, string path = FileKey, string fileState = "present", string sha1 = Sha1)
        => "{\"map\":{\"id\":" + id + ",\"name\":\"Legion TD\",\"path\":\"" + path + "\",\"temporary\":true," +
           "\"fileState\":\"" + fileState + "\",\"gameMap\":{\"sha1\":\"" + sha1 + "\"}}}";

    protected static string Verified(int mapId, string path = FileKey, string fileState = "deleted")
        => "{\"mapId\":" + mapId + ",\"path\":\"" + path + "\",\"fileState\":\"" + fileState + "\"}";

    /// <summary>update-service's upload answer; <paramref name="filePath"/> null leaves the field out.</summary>
    protected static string UsUploadBody(string metaSha1 = Sha1, string mapProofHash = ProofHash, bool twelveP = false, string filePath = FileKey)
        => "{\"id\":\"66f0\",\"mapId\":0," + (filePath == null ? "" : "\"filePath\":\"" + filePath + "\",") + "\"mapProofHash\":\"" + mapProofHash + "\"," +
           "\"metaData\":{\"sha1\":\"" + metaSha1 + "\",\"name\":\"Legion TD\",\"checksum\":1,\"crc32\":2," +
           "\"width\":128,\"height\":106,\"suggested_players\":\"4v4\",\"num_players\":16,\"twelve_p\":" +
           (twelveP ? "true" : "false") + ",\"players\":[],\"forces\":[]}}";

    protected static string CaptureJson(int slotCount = 16, string lobbyMode = "mapped-forces", int maxTeams = 2)
    {
        var forces = lobbyMode == "free"
            ? "[{\"team\":0,\"slots\":[{\"index\":0}]}]"
            : "[{\"team\":0,\"slots\":[{\"index\":0,\"color\":0}]}]";
        return CaptureJsonWithForces(forces, slotCount, lobbyMode, maxTeams);
    }

    /// <summary><paramref name="launcherVersionJson"/> is spliced into the JSON as written, escapes included.</summary>
    protected static string CaptureJsonWithForces(
        string mappedForcesJson, int slotCount = 16, string lobbyMode = "mapped-forces", int maxTeams = 2, string launcherVersionJson = "3.4.0")
        => "{\"lobbyMode\":\"" + lobbyMode + "\",\"maxTeams\":" + maxTeams + ",\"slotCount\":" + slotCount +
           ",\"mappedForces\":" + mappedForcesJson + ",\"launcherVersion\":\"" + launcherVersionJson + "\",\"capturedAt\":\"2026-09-14T09:10:39Z\"}";

    /// <summary>A fresh handler whose dedupe probe knows nothing, the start of every new-map flow.</summary>
    private protected static ScriptedHttpHandler UnknownSha1Handler()
        => new ScriptedHttpHandler().On(IsBySha1, Respond(HttpStatusCode.NotFound));

    /// <summary>A new-map flow up to and including a successful, digest-matching update-service upload.</summary>
    private protected static ScriptedHttpHandler StoredNewMapHandler()
        => UnknownSha1Handler().On(IsUsUpload, Respond(HttpStatusCode.OK, UsUploadBody()));

    /// <summary>A handler whose dedupe probe finds record 5811 with its file deleted, the start of every restore flow.</summary>
    private protected static ScriptedHttpHandler DeletedRecordHandler()
        => new ScriptedHttpHandler().On(IsBySha1, Respond(HttpStatusCode.OK, Record(5811, fileState: "deleted")));

    // ---- Running the service ----------------------------------------------------------------

    /// <summary>Only fails a broken build that would otherwise hang; no assertion depends on timing.</summary>
    protected static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    /// <summary>Every wait the compensation retries asked for in this test; nothing actually sleeps.</summary>
    private protected List<TimeSpan> CompensationWaits { get; private set; } = [];

    /// <summary>
    /// The fileKey lock every service this test creates shares, as all requests of one process do. A test may replace it
    /// (before creating services) with one whose <see cref="TemporaryMapFileKeyLock.OnContended"/> signals the test.
    /// </summary>
    private protected TemporaryMapFileKeyLock FileKeyLock { get; set; } = new();

    [SetUp]
    public void ResetPerTestState()
    {
        CompensationWaits = [];
        FileKeyLock = new TemporaryMapFileKeyLock();
    }

    /// <summary>A signal a scripted route or seam completes and the test awaits; continuations never run inline.</summary>
    protected static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// What Program.cs registers before AddMapServices, as doubles: the tracing interceptor and its ActivitySource,
    /// logging, the two service clients over <paramref name="handler"/>, the shared rate limiter and
    /// <paramref name="authService"/>. Shared by the DI smoke test and the real-pipeline round trips, so both exercise the
    /// production registrations over the same host surface.
    /// </summary>
    private protected static IServiceCollection AddHostDoubles(
        IServiceCollection services, ScriptedHttpHandler handler, ActivitySource activitySource, IW3CAuthenticationService authService)
    {
        services.AddSingleton(activitySource);
        services.AddSingleton<TracingInterceptor>();
        services.AddLogging();
        services.AddSingleton<IHttpClientFactory>(new ScriptedHttpHandler.Factory(handler));
        services.AddInterceptedSingleton<MatchmakingServiceClient>();
        services.AddInterceptedSingleton<UpdateServiceClient>();
        services.AddSingleton<MintRateLimiter>();
        services.AddSingleton(authService);
        return services;
    }

    private protected TemporaryMapUploadService CreateService(
        ScriptedHttpHandler handler,
        MintRateLimiter limiter = null,
        ILogger<TemporaryMapUploadService> logger = null,
        Func<TimeSpan, Task> waitAsync = null)
    {
        var factory = new ScriptedHttpHandler.Factory(handler);
        return new TemporaryMapUploadService(
            new MatchmakingServiceClient(factory),
            new UpdateServiceClient(factory),
            limiter ?? new MintRateLimiter(),
            FileKeyLock,
            logger ?? NullLogger<TemporaryMapUploadService>.Instance)
        {
            SpoolDirectory = SpoolDirectory,
            WaitAsync = waitAsync ?? (delay =>
            {
                CompensationWaits.Add(delay);
                return Task.CompletedTask;
            }),
        };
    }

    /// <summary>
    /// The sweep over the real clients on <paramref name="handler"/>, purging this test's private spool directory and
    /// sharing the test's <see cref="FileKeyLock"/> with every service the test creates, as one process does.
    /// </summary>
    private protected TemporaryMapExpirySweep CreateSweep(
        ScriptedHttpHandler handler,
        ILogger<TemporaryMapExpirySweep> logger = null,
        Func<string, IEnumerable<string>> enumerateSpoolFiles = null)
    {
        var factory = new ScriptedHttpHandler.Factory(handler);
        return new TemporaryMapExpirySweep(
            new MatchmakingServiceClient(factory),
            new UpdateServiceClient(factory),
            FileKeyLock,
            logger ?? NullLogger<TemporaryMapExpirySweep>.Instance)
        {
            SpoolDirectory = SpoolDirectory,
            EnumerateSpoolFiles = enumerateSpoolFiles ?? Directory.EnumerateFiles,
        };
    }

    /// <summary>A spool file in this test's directory, last written at <paramref name="lastWriteUtc"/>.</summary>
    protected string PlantSpoolFile(string name, DateTime lastWriteUtc)
    {
        Directory.CreateDirectory(SpoolDirectory);
        var path = Path.Combine(SpoolDirectory, name + TemporaryMapUploadReader.SpoolFileExtension);
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    /// <summary>A handler on which the sweep has nothing to do: no expired map and no stored file.</summary>
    private protected static ScriptedHttpHandler NothingToDo()
        => new ScriptedHttpHandler()
            .On(HttpMethod.Get, "/maps/temporary/expired", HttpStatusCode.OK, "{\"items\":[]}")
            .On(HttpMethod.Get, "/api/content/maps/files", HttpStatusCode.OK, "{\"files\":[],\"next\":null}");

    protected static string Metadata(
        string metadataSha1 = Sha1, string capture = null, bool withCapture = true, string originalFileNameJson = "Legion TD.w3x")
        => "{\"sha1\":\"" + metadataSha1 + "\",\"originalFileName\":\"" + originalFileNameJson + "\",\"fileSize\":3" +
           (withCapture ? ",\"capture\":" + (capture ?? CaptureJson()) : "") + "}";

    /// <summary>
    /// One upload of the bytes "abc" by <paramref name="battleTag"/>. <paramref name="originalFileNameJson"/> is spliced
    /// into the JSON as written, escapes included.
    /// </summary>
    private protected Task<TemporaryMapUploadOutcome> Run(
        ScriptedHttpHandler handler,
        MintRateLimiter limiter = null,
        string metadataSha1 = Sha1,
        string capture = null,
        bool withCapture = true,
        ILogger<TemporaryMapUploadService> logger = null,
        CancellationToken cancellationToken = default,
        Func<TimeSpan, Task> waitAsync = null,
        string originalFileNameJson = "Legion TD.w3x",
        string battleTag = BattleTag)
    {
        var (body, contentType) = BuildMultipart(Metadata(metadataSha1, capture, withCapture, originalFileNameJson), "abc"u8.ToArray());
        return CreateService(handler, limiter, logger, waitAsync).HandleUploadAsync(body, contentType, battleTag, cancellationToken);
    }

    // ---- Metric ------------------------------------------------------------------------------

    /// <summary>A snapshot of website_temporary_map_uploads_total per result label, to assert what one call added.</summary>
    protected sealed class UploadCounts
    {
        private readonly Dictionary<string, double> _before = Snapshot();

        /// <summary>Each of <paramref name="results"/> was counted exactly once since the snapshot, every other label not at all.</summary>
        public void AssertCountedOnceAs(params string[] results)
        {
            var after = Snapshot();
            foreach (var label in AllResults)
            {
                Assert.That(after[label] - _before[label], Is.EqualTo(results.Contains(label) ? 1 : 0),
                    $"website_temporary_map_uploads_total{{result=\"{label}\"}} delta");
            }
        }

        public void AssertNothingCounted()
        {
            var after = Snapshot();
            foreach (var label in AllResults)
            {
                Assert.That(after[label] - _before[label], Is.Zero, $"website_temporary_map_uploads_total{{result=\"{label}\"}} delta");
            }
        }

        private static Dictionary<string, double> Snapshot()
            => AllResults.ToDictionary(label => label, label => TemporaryMapMetrics.Uploads.WithLabels(label).Value);
    }

    // ---- Logs --------------------------------------------------------------------------------

    /// <summary>
    /// Captures both what the service logs through its <see cref="ILogger{TCategoryName}"/> and what the reader and the
    /// spool handle log through the static Serilog logger, into one <see cref="CapturingLogSink"/>.
    /// </summary>
    protected sealed class LogCapture : IDisposable
    {
        private readonly Serilog.Core.Logger _serilog;
        private readonly SerilogLoggerFactory _factory;
        private readonly IDisposable _staticLogger;

        public LogCapture()
        {
            _serilog = Sink.CreateLogger();
            _factory = new SerilogLoggerFactory(_serilog);
            Logger = _factory.CreateLogger<TemporaryMapUploadService>();
            _staticLogger = Sink.CaptureStaticLogger();
        }

        public CapturingLogSink Sink { get; } = new();

        public ILogger<TemporaryMapUploadService> Logger { get; }

        /// <summary>A logger of another category over the same sink, e.g. for the controller in front of the service.</summary>
        public ILogger<T> CreateLogger<T>() => _factory.CreateLogger<T>();

        /// <summary>Every event as one line: level, rendered message, every property value and the exception text.</summary>
        public string[] Lines()
            => Sink.Events
                .Select(e => $"{e.Level} {e.RenderMessage()} | {string.Join(" | ", e.Properties.Select(p => $"{p.Key}={p.Value}"))} | {e.Exception}")
                .ToArray();

        public void Dispose()
        {
            _staticLogger.Dispose();
            _factory.Dispose();
            _serilog.Dispose();
        }
    }

    // ---- Spool directory ---------------------------------------------------------------------

    /// <summary>D8: no spool file left behind, judged as a set against what the directory held before.</summary>
    protected void AssertNoNewSpoolFiles(IEnumerable<string> before)
        => Assert.That(FilesIn(SpoolDirectory), Is.SubsetOf(before), "a spool file survived the upload");
}
