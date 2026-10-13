using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Mongo2Go;
using MongoDB.Driver;
using Moq;
using NUnit.Framework;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.Rewards.Entities;
using W3ChampionsStatisticService.Admin.CloudStorage.S3;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Rewards.Portraits;

namespace WC3ChampionsStatisticService.Tests.Rewards;

// Uses an isolated Mongo2Go instance, or the explicitly supplied disposable test container.
// Never connects to the shared IntegrationTestBase database.
[TestFixture]
public class PortraitReplacementTests
{
    private MongoDbRunner _runner;
    private MongoClient _client;
    private IMongoDatabase _database;
    private Mock<IS3Service> _storage;
    private PortraitReplacementService _service;

    [OneTimeSetUp]
    public void StartMongo()
    {
        var connection = Environment.GetEnvironmentVariable("PORTRAIT_TEST_MONGO_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connection))
        {
            _runner = MongoDbRunner.Start();
            connection = _runner.ConnectionString;
        }
        _client = new MongoClient(connection);
        _database = _client.GetDatabase("W3Champions-Statistic-Service");
    }

    [OneTimeTearDown]
    public void StopMongo() => _runner?.Dispose();

    [SetUp]
    public async Task Seed()
    {
        await _database.DropCollectionAsync(nameof(PersonalSetting));
        await _database.DropCollectionAsync(nameof(PortraitDefinition));
        await _database.DropCollectionAsync(nameof(Reward));
        _storage = new Mock<IS3Service>();
        _storage.Setup(s => s.DownloadFile("SPECIAL_121.jpg")).ReturnsAsync(new byte[] { 1 });
        _service = new PortraitReplacementService(_client, _storage.Object);
        await _database.GetCollection<PortraitDefinition>(nameof(PortraitDefinition)).InsertManyAsync(new[]
        {
            new PortraitDefinition(120, new List<string> { "Gold" }),
            new PortraitDefinition(121, new List<string> { "Custom" }),
            new PortraitDefinition(122, new List<string>())
        });
    }

    private static PersonalSetting Player(string tag, bool ownsOld, AvatarCategory category, long selectedId) => new(tag)
    {
        RaceWins = null,
        SpecialPictures = ownsOld ? new[] { new SpecialPicture(120, "Personal tooltip") } : Array.Empty<SpecialPicture>(),
        ProfilePicture = new ProfilePicture { Race = category, PictureId = selectedId, IsClassic = true },
        CountryCode = "CA"
    };

    private Task SavePlayers(params PersonalSetting[] players) =>
        _database.GetCollection<PersonalSetting>(nameof(PersonalSetting)).InsertManyAsync(players);

    private Task<PersonalSetting> Load(string tag) =>
        _database.GetCollection<PersonalSetting>(nameof(PersonalSetting)).Find(p => p.Id == tag).FirstAsync();

    [Test]
    public async Task ReplacementGrantsAllOwnersAndOnlySwitchesSelectedSpecialPortrait()
    {
        var alreadyOwnsNew = Player("Both#1", true, AvatarCategory.Special, 120);
        alreadyOwnsNew.SpecialPictures = new[] { new SpecialPicture(120, "Old"), new SpecialPicture(121, "Existing new tooltip") };
        await SavePlayers(
            Player("Selected#1", true, AvatarCategory.Special, 120),
            Player("Other#1", true, AvatarCategory.Starter, 1),
            Player("SameNumber#1", true, AvatarCategory.HU, 120),
            Player("Unrelated#1", false, AvatarCategory.Starter, 1), alreadyOwnsNew);

        var preview = await _service.Preview(120);
        Assert.That(preview.Owners, Is.EqualTo(4));
        Assert.That(preview.SelectedProfiles, Is.EqualTo(2));
        var result = await _service.Replace(120, 121);
        Assert.That(result.UpdatedPlayers, Is.EqualTo(4));
        var selected = await Load("Selected#1");
        Assert.That(selected.SpecialPictures.Select(p => p.PictureId), Is.EquivalentTo(new[] { 120, 121 }));
        Assert.That(selected.SpecialPictures.Single(p => p.PictureId == 121).Description, Is.EqualTo("Personal tooltip"));
        Assert.That(selected.ProfilePicture.PictureId, Is.EqualTo(121));
        Assert.That(selected.ProfilePicture.IsClassic, Is.True);
        Assert.That(selected.CountryCode, Is.EqualTo("CA"));
        Assert.That(selected.LastUpdated, Is.GreaterThan(DateTimeOffset.MinValue));
        Assert.That((await Load("Other#1")).ProfilePicture.PictureId, Is.EqualTo(1));
        Assert.That((await Load("SameNumber#1")).ProfilePicture.PictureId, Is.EqualTo(120));
        Assert.That((await Load("Unrelated#1")).SpecialPictures, Is.Empty);
        Assert.That((await Load("Both#1")).SpecialPictures.Count(p => p.PictureId == 121), Is.EqualTo(1));
        Assert.That((await Load("Both#1")).SpecialPictures.Single(p => p.PictureId == 121).Description, Is.EqualTo("Existing new tooltip"));
        Assert.That((await _service.Replace(120, 121)).UpdatedPlayers, Is.Zero);
        Assert.That((await Load("Selected#1")).LastUpdated, Is.EqualTo(selected.LastUpdated));
    }

    [Test]
    public async Task CopiesGroupsAndUpdatesRewardReferencesWithoutChangingOtherParameters()
    {
        var reward = new Reward
        {
            ModuleId = "portrait_reward",
            DisplayId = "Gold",
            IsActive = true,
            Parameters = new Dictionary<string, object> { ["portraitIds"] = new[] { 120, 121, 122 }, ["other"] = "untouched" }
        };
        await _database.GetCollection<Reward>(nameof(Reward)).InsertOneAsync(reward);
        Assert.That((await _service.Preview(120)).RewardDefinitions, Is.EqualTo(1));
        Assert.That((await _service.Replace(120, 121)).UpdatedRewards, Is.EqualTo(1));
        var stored = await _database.GetCollection<Reward>(nameof(Reward)).Find(r => r.Id == reward.Id).FirstAsync();
        var ids = System.Text.Json.JsonSerializer.SerializeToElement(stored.Parameters["portraitIds"]).EnumerateArray().Select(v => v.GetInt32());
        Assert.That(ids, Is.EquivalentTo(new[] { 121, 122 }));
        Assert.That(stored.Parameters["other"], Is.EqualTo("untouched"));
        var replacement = await _database.GetCollection<PortraitDefinition>(nameof(PortraitDefinition)).Find(p => p.Id == "121").FirstAsync();
        Assert.That(replacement.Groups, Is.EquivalentTo(new[] { "Gold", "Custom" }));
        Assert.That((await _service.Preview(120)).ReplacementPortraitId, Is.EqualTo(121));
        Assert.That((await _service.Replace(120, 121)).UpdatedRewards, Is.Zero);
    }

    [Test]
    public async Task MissingS3ImageLeavesPlayersAndReplacementHistoryUntouched()
    {
        await SavePlayers(Player("Selected#1", true, AvatarCategory.Special, 120));
        _storage.Setup(s => s.DownloadFile("SPECIAL_121.jpg")).ThrowsAsync(new FileNotFoundException());
        Assert.ThrowsAsync<FileNotFoundException>(() => _service.Replace(120, 121));
        Assert.That((await Load("Selected#1")).ProfilePicture.PictureId, Is.EqualTo(120));
        Assert.That((await _service.Preview(120)).ReplacementPortraitId, Is.Null);
    }

    [Test]
    public async Task RetryFinishesAnInterruptedReplacementAndRejectsAnotherTarget()
    {
        var definitions = _database.GetCollection<PortraitDefinition>(nameof(PortraitDefinition));
        await definitions.UpdateOneAsync(p => p.Id == "120", Builders<PortraitDefinition>.Update.Set(p => p.ReplacementPortraitId, 121));
        await SavePlayers(Player("Selected#1", true, AvatarCategory.Special, 120));
        Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => _service.Replace(120, 122));
        await _service.Replace(120, 121);
        Assert.That((await Load("Selected#1")).ProfilePicture.PictureId, Is.EqualTo(121));
        // Group edits must not erase the retry target.
        await new PortraitRepository(_client).UpdatePortraitDefinition(new List<int> { 120 }, new List<string> { "Edited" });
        Assert.That((await _service.Preview(120)).ReplacementPortraitId, Is.EqualTo(121));
    }

    [Test]
    public async Task RepairsSelectedPortraitEvenWhenItsOldGrantIsMissing()
    {
        var player = Player("Inconsistent#1", false, AvatarCategory.Special, 120);
        player.SpecialPictures = null;
        await SavePlayers(player);
        await _service.Replace(120, 121);
        var stored = await Load(player.Id);
        Assert.That(stored.ProfilePicture.PictureId, Is.EqualTo(121));
        Assert.That(stored.SpecialPictures.Single().PictureId, Is.EqualTo(121));
    }

    [Test]
    public void RejectsSameOrUnknownIds()
    {
        Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => _service.Replace(120, 120));
        Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => _service.Replace(120, 999));
        _storage.Verify(s => s.DownloadFile(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ListsOwnersWithSelectionAndEscapedSearchAndPagination()
    {
        await SavePlayers(
            Player("Owner[1]#1", true, AvatarCategory.Special, 120),
            Player("Owner2#1", true, AvatarCategory.Starter, 1),
            Player("Unrelated#1", false, AvatarCategory.Starter, 1));
        var first = await _service.Owners(120, 1, 1, null);
        Assert.That(first.Total, Is.EqualTo(2));
        Assert.That(first.Players.Count, Is.EqualTo(1));
        var filtered = await _service.Owners(120, 1, 25, "[1]");
        Assert.That(filtered.Total, Is.EqualTo(1));
        Assert.That(filtered.Players.Single().BattleTag, Is.EqualTo("Owner[1]#1"));
        Assert.That(filtered.Players.Single().IsSelected, Is.True);
        Assert.That(filtered.Players.Single().Tooltip, Is.EqualTo("Personal tooltip"));
        Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => _service.Owners(120, 1, 101, null));
        Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => _service.Owners(120, 0, 25, null));
    }

    [Test]
    public async Task ListsOwnersEvenAfterTheCatalogueEntryIsRemoved()
    {
        await SavePlayers(Player("Detached#1", true, AvatarCategory.Special, 120));
        await _database.GetCollection<PortraitDefinition>(nameof(PortraitDefinition)).DeleteOneAsync(p => p.Id == "120");
        var result = await _service.Owners(120, 1, 25, null);
        Assert.That(result.Total, Is.EqualTo(1));
        Assert.That(result.Players.Single().BattleTag, Is.EqualTo("Detached#1"));
        Assert.That(result.Players.Single().IsSelected, Is.True);
        Assert.That(result.Players.Single().Tooltip, Is.EqualTo("Personal tooltip"));
        Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => _service.Owners(0, 1, 25, null));
    }

    [Test]
    public async Task BatchStatusCountsOwnersOnceAndFindsReferencesWithoutCatalogueEntries()
    {
        var owner = Player("Owner#1", true, AvatarCategory.Special, 120);
        owner.SpecialPictures = new[] { new SpecialPicture(120, "one"), new SpecialPicture(120, "duplicate"), new SpecialPicture(121, "two") };
        await SavePlayers(owner, Player("SelectedOnly#1", false, AvatarCategory.Special, 120), Player("OtherCategory#1", false, AvatarCategory.Starter, 120));
        await _database.GetCollection<PortraitDefinition>(nameof(PortraitDefinition)).DeleteOneAsync(p => p.Id == "121");
        await _database.GetCollection<Reward>(nameof(Reward)).InsertOneAsync(new Reward
        {
            ModuleId = "portrait_reward",
            DisplayId = "Future grant",
            IsActive = false,
            Parameters = new Dictionary<string, object> { ["portraitIds"] = new[] { 121, 121, 123 } }
        });
        await new PersonalSettingsRepository(_client).EnsureIndexesAsync();
        var indexes = await _database.GetCollection<PersonalSetting>(nameof(PersonalSetting)).Indexes.ListAsync();
        Assert.That((await indexes.ToListAsync()).Select(index => index["name"].AsString), Does.Contain("portrait_ownership").And.Contain("selected_portrait"));
        var result = await _service.AssetStatuses(new List<int> { 120, 121, 122, 123, 124, 120 });
        Assert.That(result.Count, Is.EqualTo(5));
        Assert.That(result.Single(s => s.PortraitId == 120), Is.EqualTo(new PortraitAssetStatus(120, true, 2, 2, 0)));
        Assert.That(result.Single(s => s.PortraitId == 121), Is.EqualTo(new PortraitAssetStatus(121, false, 1, 0, 1)));
        Assert.That(result.Single(s => s.PortraitId == 122), Is.EqualTo(new PortraitAssetStatus(122, true, 0, 0, 0)));
        Assert.That(result.Single(s => s.PortraitId == 123), Is.EqualTo(new PortraitAssetStatus(123, false, 0, 0, 1)));
        Assert.That(result.Single(s => s.PortraitId == 124), Is.EqualTo(new PortraitAssetStatus(124, false, 0, 0, 0)));
        Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => _service.AssetStatuses(new List<int>()));
        Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => _service.AssetStatuses(Enumerable.Range(1, 101).ToList()));
        Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => _service.AssetStatuses(new List<int> { 0 }));
    }

    [TestCase("prod/integration/icons/specialAvatars/SPECIAL_121.jpg", "icons/specialAvatars/SPECIAL_121.jpg")]
    [TestCase("test/integration/images/banner.png", "images/banner.png")]
    [TestCase("private/file.png", null)]
    public void StoragePreviewUsesThePublicIntegrationPath(string key, string expected)
    {
        Assert.That(S3Service.GetPreviewPath(key), Is.EqualTo(expected));
    }

    [Test]
    public async Task EditingPlayerTooltipPreservesSelectionAndOtherPlayers()
    {
        await SavePlayers(Player("Selected#1", true, AvatarCategory.Special, 120),
            Player("Other#1", true, AvatarCategory.Starter, 1));
        var handler = new PortraitCommandHandler(new PersonalSettingsRepository(_client), new PortraitRepository(_client));
        await handler.UpsertSpecialPortraits(new PortraitsCommand
        {
            BnetTags = new List<string> { "Selected#1" },
            Portraits = new List<int> { 120 },
            Tooltip = "Edited tooltip"
        });
        var selected = await Load("Selected#1");
        Assert.That(selected.SpecialPictures.Single().Description, Is.EqualTo("Edited tooltip"));
        Assert.That(selected.ProfilePicture.PictureId, Is.EqualTo(120));
        Assert.That((await Load("Other#1")).SpecialPictures.Single().Description, Is.EqualTo("Personal tooltip"));
        Assert.That((await _service.Owners(120, 1, 25, "Selected")).Players.Single().Tooltip, Is.EqualTo("Edited tooltip"));
    }

    [Test]
    public async Task CatalogueRemovalAndRestorationPreserveOwnershipAndDoNotTouchStorage()
    {
        await SavePlayers(Player("Selected#1", true, AvatarCategory.Special, 120));
        var repository = new PortraitRepository(_client);
        await repository.DeletePortraitDefinitions(new List<int> { 120 });
        Assert.That((await _service.AssetStatuses(new List<int> { 120 })).Single().InCatalogue, Is.False);
        Assert.That((await _service.Owners(120, 1, 25, null)).Total, Is.EqualTo(1));
        await repository.SaveNewPortraitDefinitions(new List<int> { 120 });
        await repository.SaveNewPortraitDefinitions(new List<int> { 120 }, new List<string> { "Ignored duplicate" });
        var definitions = await repository.LoadPortraitDefinitions();
        Assert.That(definitions.Count(p => p.Id == "120"), Is.EqualTo(1));
        Assert.That(definitions.Single(p => p.Id == "120").Groups, Is.Empty);
        Assert.That((await Load("Selected#1")).ProfilePicture.PictureId, Is.EqualTo(120));
        Assert.That((await Load("Selected#1")).SpecialPictures.Single().Description, Is.EqualTo("Personal tooltip"));
        _storage.Verify(s => s.DownloadFile(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task DirectAssignmentCreatesMissingSettingsAndRemovalUpdatesSelectedProfileAndChangeFeed()
    {
        var settings = new PersonalSettingsRepository(_client);
        var handler = new PortraitCommandHandler(settings, new PortraitRepository(_client));
        await handler.UpsertSpecialPortraits(new PortraitsCommand
        {
            BnetTags = new List<string> { "FirstGrant#1" },
            Portraits = new List<int> { 120 },
            Tooltip = "Custom"
        });
        var player = await settings.Find("FirstGrant#1");
        Assert.That(player.SpecialPictures.Single().PictureId, Is.EqualTo(120));
        Assert.That(player.LastUpdated, Is.GreaterThan(DateTimeOffset.MinValue));
        player.ProfilePicture = new ProfilePicture { Race = AvatarCategory.Special, PictureId = 120 };
        await settings.Save(player);
        var before = player.LastUpdated;
        await handler.DeleteSpecialPortraits(new PortraitsCommand
        {
            BnetTags = new List<string> { player.Id },
            Portraits = new List<int> { 120 }
        });
        var removed = await settings.Find(player.Id);
        Assert.That(removed.SpecialPictures, Is.Empty);
        Assert.That(removed.ProfilePicture.Race, Is.EqualTo(AvatarCategory.Starter));
        Assert.That(removed.LastUpdated, Is.GreaterThanOrEqualTo(before));
    }
}
