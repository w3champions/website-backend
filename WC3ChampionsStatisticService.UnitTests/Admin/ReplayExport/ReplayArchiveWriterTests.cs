using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Admin.ReplayExport;

namespace WC3ChampionsStatisticService.Tests.Admin.ReplayExport;

[TestFixture]
public class ReplayArchiveWriterTests
{
    [Test]
    public async Task Write_StreamsReplaysAndListsFailuresInTheManifest()
    {
        var matches = new List<ReplayExportMatch> { Match(1, "Happy#2384", "Lyn#1"), Match(2, "a#1", "b#2"), Match(3, "c#3", "d#4") };
        var output = new MemoryStream();

        var summary = await ReplayArchiveWriter.Write(
            new NonSeekableStream(output),
            matches,
            (floId, _) => floId == 2
                ? throw new HttpRequestException("gone", null, HttpStatusCode.Gone)
                : Task.FromResult(Encoding.ASCII.GetBytes($"replay {floId}")),
            new ReplayArchiveInfo { ExportId = "export-1", Label = "Order 42", Filter = new ReplayExportFilter { Patch = "3.0.0" } },
            CancellationToken.None);

        Assert.AreEqual(2, summary.Written);
        Assert.AreEqual(1, summary.Failed);
        CollectionAssert.AreEquivalent(new[] { matches[0].Id, matches[2].Id }, summary.WrittenMatchIds);

        using var zip = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        CollectionAssert.Contains(names, "replays/2026-10-11_1v1_Happy-UD_vs_Lyn-OC_1.w3g");
        CollectionAssert.Contains(names, "manifest.json");
        Assert.AreEqual(4, names.Count, "2 replays + 2 manifests");

        using var jsonReader = new StreamReader(zip.GetEntry("manifest.json")!.Open());
        var json = await jsonReader.ReadToEndAsync();
        StringAssert.Contains("\"exportId\": \"export-1\"", json);
        StringAssert.Contains("\"label\": \"Order 42\"", json);

        using var reader = new StreamReader(zip.GetEntry("manifest.csv")!.Open());
        var csv = await reader.ReadToEndAsync();
        StringAssert.Contains(",archived,", csv);
        StringAssert.Contains(",2026-10-11T20:00:00Z,2026-10-11T20:14:30Z,", csv);
        StringAssert.Contains("Happy#2384 (UD 2000 W) | Lyn#1 (OC 1900)", csv);
    }

    [Test]
    public void ForManifest_LeavesOutTheIdsOfPastDownloads()
    {
        var filter = new ReplayExportFilter { Patch = "3.0.0", ExcludePastDownloads = true, ExcludeExportIds = ["export-1"] };

        var manifest = filter.ForManifest();

        Assert.IsEmpty(manifest.ExcludeExportIds);
        Assert.IsTrue(manifest.ExcludePastDownloads);
        Assert.AreEqual("3.0.0", manifest.Patch);
        CollectionAssert.AreEqual(new[] { "export-1" }, filter.ExcludeExportIds, "The filter of the ticket stays as it was.");
    }

    [Test]
    public void FileName_UsesTheMapForTeamGames()
    {
        var match = Match(7, "a#1", "b#2");
        match.GameMode = GameMode.GM_2v2_AT;
        match.MapName = "Northern Isles";
        match.Teams[0].Players.Add(new ReplayExportPlayer { BattleTag = "x#9", Race = Race.HU });

        Assert.AreEqual("2026-10-11_2v2_AT_NorthernIsles_7.w3g", ReplayArchiveWriter.FileName(match));
    }

    private static ReplayExportMatch Match(int floId, string tag1, string tag2) => new()
    {
        Id = $"{floId:D24}",
        FloMatchId = floId,
        GameMode = GameMode.GM_1v1,
        StartTime = new DateTimeOffset(2026, 10, 11, 20, 0, 0, TimeSpan.Zero),
        EndTime = new DateTimeOffset(2026, 10, 11, 20, 14, 30, TimeSpan.Zero),
        Teams =
        [
            new ReplayExportTeam { Players = [new ReplayExportPlayer { BattleTag = tag1, Race = Race.RnD, RndRace = Race.UD, OldMmr = 2000, Won = true }] },
            new ReplayExportTeam { Players = [new ReplayExportPlayer { BattleTag = tag2, Race = Race.OC, OldMmr = 1900 }] },
        ],
    };

    /// <summary>The HTTP response body cannot seek; ZipArchive must handle that.</summary>
    private class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}
