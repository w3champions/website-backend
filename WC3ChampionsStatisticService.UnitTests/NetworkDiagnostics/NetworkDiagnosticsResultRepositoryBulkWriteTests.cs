using System;
using System.Linq;
using System.Net;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics.Persistence;
using W3ChampionsStatisticService.NetworkDiagnostics.Results;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

/// <summary>Which failures of the IP-info upsert are the harmless "someone else inserted the same row first".</summary>
[TestFixture]
public class NetworkDiagnosticsResultRepositoryBulkWriteTests
{
    private const int DuplicateKeyCode = 11000;

    private static readonly ConnectionId Connection = new(new ServerId(new ClusterId(1), new DnsEndPoint("localhost", 27017)));

    // The driver builds these from server replies only; their constructors are not public.
    private static T Create<T>(params object[] args) =>
        (T)typeof(T).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(c => c.GetParameters().Length == args.Length)
            .Invoke(args);

    private static BulkWriteError WriteError(int index, int code) =>
        Create<BulkWriteError>(index, code == DuplicateKeyCode ? ServerErrorCategory.DuplicateKey : ServerErrorCategory.Uncategorized, code, "write failed", new BsonDocument());

    private static MongoBulkWriteException<NetworkDiagnosticIpInfo> Failure(BulkWriteError[] writeErrors, bool writeConcernError) =>
        new(Connection, result: null, writeErrors,
            writeConcernError ? Create<WriteConcernError>(64, "WriteConcernFailed", "waiting for replication timed out", new BsonDocument(), Array.Empty<string>()) : null,
            unprocessedRequests: []);

    [Test]
    public void EveryWriteErrorIsADuplicateKey_IsTheLostRace()
    {
        Assert.IsTrue(NetworkDiagnosticsResultRepository.OnlyDuplicateKeys(Failure([WriteError(0, DuplicateKeyCode), WriteError(2, DuplicateKeyCode)], writeConcernError: false)));
    }

    // A bulk write that failed on its write concern alone has no write errors at all: "all of none" must not make
    // it look like a duplicate.
    [Test]
    public void OnlyAWriteConcernError_IsNotADuplicate()
    {
        Assert.IsFalse(NetworkDiagnosticsResultRepository.OnlyDuplicateKeys(Failure([], writeConcernError: true)));
    }

    [Test]
    public void NoErrorsAtAll_IsNotADuplicate()
    {
        Assert.IsFalse(NetworkDiagnosticsResultRepository.OnlyDuplicateKeys(Failure([], writeConcernError: false)));
    }

    [Test]
    public void ADuplicateKeyNextToAnotherWriteError_IsNotSwallowed()
    {
        Assert.IsFalse(NetworkDiagnosticsResultRepository.OnlyDuplicateKeys(Failure([WriteError(0, DuplicateKeyCode), WriteError(1, 121)], writeConcernError: false)));
    }
}
