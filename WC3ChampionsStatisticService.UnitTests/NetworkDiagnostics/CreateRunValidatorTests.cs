using System.Linq;
using NUnit.Framework;
using W3ChampionsStatisticService.NetworkDiagnostics.Runs;

namespace WC3ChampionsStatisticService.Tests.NetworkDiagnostics;

[TestFixture]
public class CreateRunValidatorTests
{
    private static CreateRunRequest Valid() => CreateRunValidator.Normalize(new CreateRunRequest
    {
        Audience = new RunAudience { Kind = AudienceKind.Users, BattleTags = ["Name#1234"] },
        Targets = new RunTargets { FloNodeIds = [12] },
        Methods = ["IcmpMtr", "TcpPing"],
    });

    private static string Validate(CreateRunRequest r) => CreateRunValidator.Validate(CreateRunValidator.Normalize(r));

    [Test]
    public void Valid_PassesAndNormalizes()
    {
        var r = Valid();
        Assert.IsNull(CreateRunValidator.Validate(r));
        Assert.AreEqual(60, r.WindowSeconds);
        Assert.IsNotNull(r.Targets.CoreServers);
        Assert.IsNotNull(r.Targets.CustomTargets);
    }

    [Test]
    public void Normalize_DefaultsCustomPortTo443_AndDedupesTagsAndMethods()
    {
        var r = CreateRunValidator.Normalize(Valid() with
        {
            Audience = new RunAudience { Kind = AudienceKind.Users, BattleTags = ["A#1234", "A#1234", "B#5678"] },
            Targets = new RunTargets { CustomTargets = [new CustomTarget { Ip = "10.0.0.5" }] },
            Methods = ["TcpPing", "TcpPing"],
        });
        Assert.AreEqual(443, r.Targets.CustomTargets.Single().TcpPort);
        Assert.AreEqual(new[] { "A#1234", "B#5678" }, r.Audience.BattleTags);
        Assert.AreEqual(new[] { "TcpPing" }, r.Methods);
    }

    [Test] public void MissingAudience() => Assert.AreEqual("audience is required", Validate(Valid() with { Audience = null }));
    [Test] public void MissingTargets() => Assert.AreEqual("targets is required", Validate(Valid() with { Targets = null }));
    [Test] public void EmptyMethods() => Assert.AreEqual("methods must not be empty", Validate(Valid() with { Methods = [] }));
    [Test] public void UnknownMethod() => Assert.AreEqual("unknown method: Ping", Validate(Valid() with { Methods = ["Ping"] }));

    [TestCase(29)]
    [TestCase(301)]
    public void WindowOutOfRange(int s) => Assert.AreEqual("windowSeconds must be in 30-300", Validate(Valid() with { WindowSeconds = s }));

    [TestCase(30)]
    [TestCase(300)]
    public void WindowBounds(int s) => Assert.IsNull(Validate(Valid() with { WindowSeconds = s }));

    [Test]
    public void Users_NeedOneToFiveHundredTags()
    {
        Assert.AreEqual("battleTags must hold 1-500 tags", Validate(Valid() with { Audience = new RunAudience { Kind = AudienceKind.Users, BattleTags = [] } }));
        var many = Enumerable.Range(0, 501).Select(i => $"Name#{1000 + i}").ToList();
        Assert.AreEqual("battleTags must hold 1-500 tags", Validate(Valid() with { Audience = new RunAudience { Kind = AudienceKind.Users, BattleTags = many } }));
    }

    [TestCase("name")]
    [TestCase("N#1234")]
    [TestCase("Name#12")]
    [TestCase("Na me#1234")]
    [TestCase("Name#1234567")]
    public void InvalidTag(string tag) =>
        Assert.AreEqual($"invalid battleTag: {tag}", Validate(Valid() with { Audience = new RunAudience { Kind = AudienceKind.Users, BattleTags = [tag] } }));

    [Test]
    public void TagIsNotCaseFolded() =>
        Assert.AreEqual(new[] { "name#1234", "Name#1234" },
            CreateRunValidator.Normalize(Valid() with { Audience = new RunAudience { Kind = AudienceKind.Users, BattleTags = ["name#1234", "Name#1234"] } }).Audience.BattleTags);

    [TestCase(null)]
    [TestCase(0)]
    public void SampleCountBelowOne(int? count) =>
        Assert.AreEqual("count must be >= 1", Validate(Valid() with { Audience = new RunAudience { Kind = AudienceKind.SampleCount, Count = count } }));

    [TestCase(0.0)]
    [TestCase(100.5)]
    public void PercentOutOfRange(double p) =>
        Assert.AreEqual("percent must be in (0, 100]", Validate(Valid() with { Audience = new RunAudience { Kind = AudienceKind.SamplePercent, Percent = p } }));

    [Test] public void PercentHundredOk() => Assert.IsNull(Validate(Valid() with { Audience = new RunAudience { Kind = AudienceKind.SamplePercent, Percent = 100 } }));
    [Test] public void AllOk() => Assert.IsNull(Validate(Valid() with { Audience = new RunAudience { Kind = AudienceKind.All } }));
    [Test] public void UnknownKind() => Assert.AreEqual("unknown audience kind", Validate(Valid() with { Audience = new RunAudience { Kind = "Everyone" } }));

    [Test]
    public void NoTargets() => Assert.AreEqual("at least one target is required", Validate(Valid() with { Targets = new RunTargets() }));

    [Test]
    public void GatewayAloneIsATarget() => Assert.IsNull(Validate(Valid() with { Targets = new RunTargets { IncludeDefaultGateway = true } }));

    [TestCase("10.0.0", 443, "invalid custom target ip: 10.0.0")]
    [TestCase("10.0.0.5", 0, "custom target port must be in 1-65535")]
    [TestCase("10.0.0.5", 65536, "custom target port must be in 1-65535")]
    public void BadCustomTarget(string ip, int port, string expected) =>
        Assert.AreEqual(expected, Validate(Valid() with { Targets = new RunTargets { CustomTargets = [new CustomTarget { Ip = ip, TcpPort = port }] } }));

    [Test]
    public void PrivateCustomIpIsAllowed() =>
        Assert.IsNull(Validate(Valid() with { Targets = new RunTargets { CustomTargets = [new CustomTarget { Ip = "192.168.0.10", TcpPort = 22 }] } }));

    [Test]
    public void TooManyCustomTargets()
    {
        var custom = Enumerable.Range(0, 51).Select(i => new CustomTarget { Ip = $"10.0.0.{i}", TcpPort = 443 }).ToList();
        Assert.AreEqual("at most 50 custom targets", Validate(Valid() with { Targets = new RunTargets { CustomTargets = custom } }));
    }
}
