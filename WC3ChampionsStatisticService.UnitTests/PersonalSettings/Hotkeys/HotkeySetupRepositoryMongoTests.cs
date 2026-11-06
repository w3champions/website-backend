using System;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.PersonalSettings.Hotkeys;

namespace WC3ChampionsStatisticService.Tests.PersonalSettings.Hotkeys;

[TestFixture]
public class HotkeySetupRepositoryMongoTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 11, 5, 12, 30, 45, 123, TimeSpan.Zero);
    private MongoDbRunner _runner;
    private HotkeySetupRepository _repo;

    [SetUp]
    public void SetUp()
    {
        _runner = MongoDbRunner.Start();
        _repo = new HotkeySetupRepository(new MongoClient(_runner.ConnectionString));
    }

    [TearDown]
    public void TearDown() => _runner.Dispose();

    private static HotkeySetup Setup(string id = "Peter#1234", long revision = 1, string customKeys = "[Hotkeys]\nA=B", string prefs = "[Preferences]\nx=1") => new()
    {
        Id = id,
        Mode = 2,
        CustomKeys = customKeys,
        PreferenceSettings = prefs,
        Revision = revision,
        UpdatedAt = Stamp,
    };

    [Test]
    public async Task Load_Absent_ReturnsNull()
    {
        Assert.IsNull(await _repo.Load("Nobody#1"));
    }

    [Test]
    public async Task TryCreate_Then_Load_RoundTripsAllFields()
    {
        Assert.IsTrue(await _repo.TryCreate(Setup()));

        var loaded = await _repo.Load("Peter#1234");
        Assert.AreEqual("Peter#1234", loaded.Id);
        Assert.AreEqual(2, loaded.Mode);
        Assert.AreEqual("[Hotkeys]\nA=B", loaded.CustomKeys);
        Assert.AreEqual("[Preferences]\nx=1", loaded.PreferenceSettings);
        Assert.AreEqual(1, loaded.Revision);
        Assert.AreEqual(Stamp.ToUnixTimeMilliseconds(), loaded.UpdatedAt.ToUnixTimeMilliseconds());
    }

    [Test]
    public async Task TryCreate_NullCustomKeys_RoundTripsNull()
    {
        Assert.IsTrue(await _repo.TryCreate(Setup(customKeys: null)));

        Assert.IsNull((await _repo.Load("Peter#1234")).CustomKeys);
    }

    [Test]
    public async Task TryCreate_Twice_SecondReturnsFalse()
    {
        Assert.IsTrue(await _repo.TryCreate(Setup()));
        Assert.IsFalse(await _repo.TryCreate(Setup(prefs: "other")));

        Assert.AreEqual("[Preferences]\nx=1", (await _repo.Load("Peter#1234")).PreferenceSettings);
    }

    [Test]
    public async Task TryReplace_MatchingRevision_ReturnsTrueAndPersists()
    {
        await _repo.TryCreate(Setup());

        var next = Setup(revision: 2, prefs: "changed");
        Assert.IsTrue(await _repo.TryReplace(next, 1));

        var loaded = await _repo.Load("Peter#1234");
        Assert.AreEqual(2, loaded.Revision);
        Assert.AreEqual("changed", loaded.PreferenceSettings);
    }

    [Test]
    public async Task TryReplace_StaleRevision_ReturnsFalseAndLeavesDocument()
    {
        await _repo.TryCreate(Setup());
        Assert.IsTrue(await _repo.TryReplace(Setup(revision: 2, prefs: "current"), 1));

        Assert.IsFalse(await _repo.TryReplace(Setup(revision: 2, prefs: "stale"), 1));

        var loaded = await _repo.Load("Peter#1234");
        Assert.AreEqual(2, loaded.Revision);
        Assert.AreEqual("current", loaded.PreferenceSettings);
    }

    [Test]
    public async Task TryReplace_Absent_ReturnsFalse()
    {
        Assert.IsFalse(await _repo.TryReplace(Setup(revision: 2), 1));
        Assert.IsNull(await _repo.Load("Peter#1234"));
    }
}
