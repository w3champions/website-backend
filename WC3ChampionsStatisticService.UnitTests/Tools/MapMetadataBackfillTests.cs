using System.Collections.Generic;
using NUnit.Framework;
using W3ChampionsStatisticService.Tools.MapMetadataBackfill;

namespace WC3ChampionsStatisticService.Tests.Tools;

public class MapMetadataBackfillTests
{
    [Test]
    public void Resolve_PrefersExactLegacyEchoIslesOverV2Family()
    {
        var resolver = new MapMetadataResolver(new[]
        {
            new SourceMapMetadata("EchoIsles", "Echo Isles", 2, 11, 11, new[] { 1 }, 10),
            new SourceMapMetadata("EchoIslesv2_2", "Echo Isles 2.0", 1051, 12, 12, new[] { 1 }, 10)
        });

        var resolution = resolver.Resolve("EchoIsles");

        Assert.AreEqual(MapMetadataResolutionStatus.Resolved, resolution.Status);
        Assert.AreEqual("exact", resolution.Confidence);
        Assert.AreEqual("Echo Isles", resolution.MapName);
        Assert.AreEqual(2, resolution.MapId);
    }

    [Test]
    public void Resolve_UsesEarliestNameWhenOneMapIdHasLaterDisplayName()
    {
        var resolver = new MapMetadataResolver(new[]
        {
            new SourceMapMetadata("AutumnLeavesv2-0", "Autumn Leaves", 44, 11, 11, new[] { 1 }, 10),
            new SourceMapMetadata("2404091839AutumnLeavesv2_0", "Autumn Leaves v2", 44, 20, 20, new[] { 1 }, 10)
        });

        var resolution = resolver.Resolve("AutumnLeavesv2_0");

        Assert.AreEqual(MapMetadataResolutionStatus.Resolved, resolution.Status);
        Assert.AreEqual("Autumn Leaves", resolution.MapName);
        Assert.AreEqual(44, resolution.MapId);
    }

    [Test]
    public void Resolve_ReportsAmbiguousFamilyWhenMultipleMapIdsMatch()
    {
        var resolver = new MapMetadataResolver(new[]
        {
            new SourceMapMetadata("SomeMapv1_0", "Some Map", 10, 11, 11, new[] { 1 }, 10),
            new SourceMapMetadata("SomeMapv2_0", "Some Map v2", 11, 12, 12, new[] { 1 }, 10)
        }, new Dictionary<string, ManualMapMetadata>());

        var resolution = resolver.Resolve("SomeMap");

        Assert.AreEqual(MapMetadataResolutionStatus.Ambiguous, resolution.Status);
    }

    [Test]
    public void Resolve_UsesManualMetadataForOldOnlyMap()
    {
        var resolver = new MapMetadataResolver(new SourceMapMetadata[] { });

        var resolution = resolver.Resolve("RuinsOfAzshara");

        Assert.AreEqual(MapMetadataResolutionStatus.Resolved, resolution.Status);
        Assert.AreEqual("manual", resolution.Confidence);
        Assert.AreEqual("Ruins of Azshara", resolution.MapName);
        Assert.IsNull(resolution.MapId);
    }

    [TestCase("3c2511040950LastRefugev1_5", "lastrefugev15")]
    [TestCase("s13_1LastRefugev1_5", "lastrefugev15")]
    [TestCase("EchoIslesv2_2w3c26012513571051", "echoislesv22")]
    [TestCase("AutumnLeavesv2-0", "autumnleavesv20")]
    public void StableKey_RemovesUploadNoiseButKeepsVersionSignal(string input, string expected)
    {
        Assert.AreEqual(expected, MapKeyNormalizer.StableKey(input));
    }

    [Test]
    public void Parse_DefaultsToDryRunWithConsolePreview()
    {
        var options = MapMetadataBackfillOptions.Parse(new[] { "--connection-string", "mongodb://localhost:27017" });

        Assert.IsFalse(options.Apply);
        Assert.AreEqual(25, options.PreviewLimit);
    }

    [Test]
    public void Parse_AllowsPreviewToBeHidden()
    {
        var options = MapMetadataBackfillOptions.Parse(new[] { "--connection-string", "mongodb://localhost:27017", "--preview-limit", "0" });

        Assert.AreEqual(0, options.PreviewLimit);
    }

    [Test]
    public void Parse_SeasonSetsSingleTargetSeason()
    {
        var options = MapMetadataBackfillOptions.Parse(new[]
        {
            "--connection-string",
            "mongodb://localhost:27017",
            "--season",
            "3"
        });

        Assert.AreEqual(3, options.TargetMinSeason);
        Assert.AreEqual(3, options.TargetMaxSeason);
    }
}
