using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics.Api;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class ObservationStreamResultTests
{
    private DefaultHttpContext _http;
    private MemoryStream _body;
    private RecordingLifetime _lifetime;

    [SetUp]
    public void SetUp()
    {
        _body = new MemoryStream();
        _lifetime = new RecordingLifetime();
        _http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        _http.Response.Body = _body;
        _http.Features.Set<IHttpRequestLifetimeFeature>(_lifetime);
    }

    private static NetworkDiagnosticObservation Obs(string measurementId) => new() { Id = ObjectId.GenerateNewId(), MeasurementId = measurementId };

    private Task Execute(IAsyncCursor<NetworkDiagnosticObservation> rows, ObservationFormat format) =>
        new ObservationStreamResult(rows, format).ExecuteResultAsync(new ActionContext { HttpContext = _http });

    private string Written => Encoding.UTF8.GetString(_body.ToArray());

    [Test]
    public async Task WritesThePreambleThenEveryBatch_AndEndsNormally()
    {
        var rows = new FakeCursor<NetworkDiagnosticObservation>(() => [Obs("m1")], () => [Obs("m2"), Obs("m3")]);

        await Execute(rows, ObservationFormats.Csv);

        Assert.AreEqual(ObservationText.ToCsv([ObservationDto.From(Obs("m1")), ObservationDto.From(Obs("m2")), ObservationDto.From(Obs("m3"))]), Written);
        Assert.AreEqual((200, "text/csv; charset=utf-8"), (_http.Response.StatusCode, _http.Response.ContentType));
        Assert.IsFalse(_lifetime.Aborted);
    }

    // What was written stays written, but the transfer must not end as if it were complete.
    [Test]
    public async Task DatabaseErrorAfterTheFirstBatch_AbortsTheConnection_InsteadOfEndingTheBody()
    {
        var rows = new FakeCursor<NetworkDiagnosticObservation>(() => [Obs("m1")], () => throw new MongoException("connection lost"));

        await Execute(rows, ObservationFormats.Ndjson);

        Assert.IsTrue(_lifetime.Aborted);
        StringAssert.StartsWith("""{"measurementId":"m1",""", Written);
    }

    // A caller that hung up: reading stops with the request's token, and that is not a failure.
    [Test]
    public async Task ClientGone_StopsReading_WithoutAbortOrError()
    {
        using var gone = new CancellationTokenSource();
        _lifetime.RequestAborted = gone.Token;
        var rows = new FakeCursor<NetworkDiagnosticObservation>(
            () =>
            {
                gone.Cancel();
                return [Obs("m1")];
            },
            () => [Obs("m2")]);

        await Execute(rows, ObservationFormats.Ndjson);

        Assert.AreEqual(gone.Token, rows.LastToken, "the cursor is read with the request's token");
        Assert.AreEqual(1, rows.BatchesRead);
        Assert.IsFalse(_lifetime.Aborted);
    }

    private sealed class RecordingLifetime : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; }
        public bool Aborted { get; private set; }
        public void Abort() => Aborted = true;
    }
}
