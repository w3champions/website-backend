using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using NUnit.Framework;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Rewards.Portraits;

namespace WC3ChampionsStatisticService.Tests.Rewards;

[TestFixture]
public class PortraitCommandTests : IntegrationTestBase
{
    [Test]
    public void UpdateSpecialPicture_Success()
    {
        var playerTag = "cepheid#1467";
        var personalSettings = new PersonalSetting(playerTag);

        List<SpecialPicture> specialPictures = [new SpecialPicture(1, "one")];
        personalSettings.UpdateSpecialPictures(specialPictures.ToArray());

        Assert.AreEqual(1, personalSettings.SpecialPictures.Length);
        Assert.AreEqual(specialPictures.First().PictureId, personalSettings.SpecialPictures.First().PictureId);

        specialPictures.RemoveAll(x => x.PictureId == 1);
        personalSettings.UpdateSpecialPictures(specialPictures.ToArray());

        Assert.AreEqual(0, personalSettings.SpecialPictures.Length);
    }

    [Test]
    public async Task AssignOnePortrait_PlayerDoesNotHave_Success()
    {
        var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
        var portraitRepo = new PortraitRepository(MongoClient);
        var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, portraitRepo);

        List<int> validPortraits = [5];
        await portraitCommandHandler.AddPortraitDefinitions(CreatePortraitsDefinitionCommand(validPortraits, []));

        var playerTag = "cepheid#1467";
        var personalSettings = new PersonalSetting(playerTag);
        await personalSettingsRepository.Save(personalSettings);

        var portraitsCommand = new PortraitsCommand();
        portraitsCommand.Portraits.Add(5);
        portraitsCommand.BnetTags.Add(playerTag);
        portraitsCommand.Tooltip = "testTooltip";

        await portraitCommandHandler.UpsertSpecialPortraits(portraitsCommand);

        var settings = await personalSettingsRepository.LoadOrCreate(playerTag);

        Assert.AreEqual(1, settings.SpecialPictures.Length);
        Assert.AreEqual(5, settings.SpecialPictures.First().PictureId);
        Assert.AreEqual("testTooltip", settings.SpecialPictures.First().Description);
    }

    [Test]
    public async Task AssignOnePortrait_PlayerDoesNotHaveSpecialPortraits_DoesNotThrow()
    {
        var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
        var portraitRepo = new PortraitRepository(MongoClient);
        var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, portraitRepo);

        List<int> validPortraits = [5];
        await portraitCommandHandler.AddPortraitDefinitions(CreatePortraitsDefinitionCommand(validPortraits, []));

        var playerTag = "cepheid#1467";
        var personalSettings = new PersonalSetting(playerTag);
        await personalSettingsRepository.Save(personalSettings);

        var portraitsCommand = new PortraitsCommand();
        portraitsCommand.Portraits.Add(5);
        portraitsCommand.BnetTags.Add(playerTag);
        portraitsCommand.Tooltip = "testTooltip";

        Assert.DoesNotThrowAsync(async () => await portraitCommandHandler.UpsertSpecialPortraits(portraitsCommand));

        var settings = await personalSettingsRepository.LoadOrCreate(playerTag);

        Assert.AreEqual(1, settings.SpecialPictures.Length);
        Assert.AreEqual(5, settings.SpecialPictures.First().PictureId);
        Assert.AreEqual("testTooltip", settings.SpecialPictures.First().Description);
    }

    [Test]
    public async Task AssignOnePortrait_PlayerAlreadyHas_TooltipUpdated()
    {
        var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
        var portraitRepo = new PortraitRepository(MongoClient);
        var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, portraitRepo);

        List<int> validPortraits = [3];
        await portraitCommandHandler.AddPortraitDefinitions(CreatePortraitsDefinitionCommand(validPortraits, []));

        var playerTag = "cepheid#1467";
        var personalSettings = new PersonalSetting(playerTag);
        personalSettings.SpecialPictures = [.. personalSettings.SpecialPictures, new SpecialPicture(3, "initialTestDescription")];
        await personalSettingsRepository.Save(personalSettings);

        var portraitsCommand = new PortraitsCommand();
        portraitsCommand.Portraits.Add(3);
        portraitsCommand.BnetTags.Add(playerTag);
        portraitsCommand.Tooltip = "testTooltip";

        await portraitCommandHandler.UpsertSpecialPortraits(portraitsCommand);

        var settings = await personalSettingsRepository.LoadOrCreate(playerTag);

        Assert.AreEqual(1, settings.SpecialPictures.Length);
        Assert.AreEqual(3, settings.SpecialPictures.First().PictureId);
        Assert.AreEqual("testTooltip", settings.SpecialPictures.First().Description);
    }

    [Ignore("Not case insensitive anymore to improve performance.")]
    [Test]
    public async Task AssignOnePortrait_PlayerDoesNotHave_CaseInsensitiveTag_Success()
    {
        var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
        var portraitRepo = new PortraitRepository(MongoClient);
        var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, portraitRepo);

        List<int> validPortraits = [5];
        await portraitCommandHandler.AddPortraitDefinitions(CreatePortraitsDefinitionCommand(validPortraits, []));

        var playerTagA = "CePhEiD#1467";
        var playerTagB = "cEpHeId#1467";
        var personalSettings = new PersonalSetting(playerTagB);
        await personalSettingsRepository.Save(personalSettings);

        var portraitsCommand = new PortraitsCommand();
        portraitsCommand.Portraits.Add(5);
        portraitsCommand.BnetTags.Add(playerTagA);
        portraitsCommand.Tooltip = "testTooltip";

        await portraitCommandHandler.UpsertSpecialPortraits(portraitsCommand);

        var settings = await personalSettingsRepository.LoadOrCreate(playerTagA);

        Assert.AreEqual(1, settings.SpecialPictures.Length);
        Assert.AreEqual(5, settings.SpecialPictures.First().PictureId);
        Assert.AreEqual("testTooltip", settings.SpecialPictures.First().Description);
    }

    [Test]
    public async Task AssignOnePortraitToMultipleTags_PlayersDoNotHave_Success()
    {
        var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
        var portraitRepo = new PortraitRepository(MongoClient);
        var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, portraitRepo);

        List<int> validPortraits = [8];
        await portraitCommandHandler.AddPortraitDefinitions(CreatePortraitsDefinitionCommand(validPortraits, []));

        var listOfSettings = new List<PersonalSetting>();
        string[] playerTags = ["cepheid#1467", "modmoto#123", "toxi#4321"];

        listOfSettings.Add(new PersonalSetting(playerTags[0]));
        listOfSettings.Add(new PersonalSetting(playerTags[1]));
        listOfSettings.Add(new PersonalSetting(playerTags[2]));

        await personalSettingsRepository.SaveMany(listOfSettings);

        var portraitsCommand = new PortraitsCommand();
        portraitsCommand.Portraits.Add(8);
        portraitsCommand.BnetTags = playerTags.AsEnumerable().ToList();
        portraitsCommand.Tooltip = "multipleTestTooltip";

        await portraitCommandHandler.UpsertSpecialPortraits(portraitsCommand);

        var settingsList = await personalSettingsRepository.LoadMany(playerTags);

        Assert.AreEqual(3, settingsList.Count);
        Assert.AreEqual(8, settingsList.First().SpecialPictures.First().PictureId);
        Assert.AreEqual(1, settingsList.First().SpecialPictures.Length);
        Assert.AreEqual(3, settingsList.Count(x => x.SpecialPictures.Length == 1));
        Assert.AreEqual("multipleTestTooltip", settingsList.Last().SpecialPictures.First().Description);
    }

    [Test]
    public async Task AssignMultiplePortraitsToMultipleTags_SomePlayersAlreadyHave_UpsertsProcessCorrectly()
    {
        var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
        var portraitRepo = new PortraitRepository(MongoClient);
        var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, portraitRepo);

        List<int> validPortraits = [1, 50, 500, 5000];
        await portraitCommandHandler.AddPortraitDefinitions(CreatePortraitsDefinitionCommand(validPortraits, []));

        var listOfSettings = new List<PersonalSetting>();
        string[] playerTags = ["cepheid#1467", "modmoto#123", "toxi#4321"];
        listOfSettings.Add(new PersonalSetting(playerTags[0]));
        listOfSettings.Add(new PersonalSetting(playerTags[1]));
        listOfSettings.Add(new PersonalSetting(playerTags[2]));
        listOfSettings.First().SpecialPictures = [.. listOfSettings.First().SpecialPictures, new SpecialPicture(50, "fifty")];
        await personalSettingsRepository.SaveMany(listOfSettings);

        var portraitIds = new List<int>
        {
            1,
            50,
            500,
            5000
        };

        var portraitsCommand = new PortraitsCommand
        {
            Portraits = portraitIds,
            BnetTags = [.. playerTags],
            Tooltip = "allTagsUpdatedWithThis"
        };

        await portraitCommandHandler.UpsertSpecialPortraits(portraitsCommand);

        var settingsList = await personalSettingsRepository.LoadMany(playerTags);

        Assert.AreEqual(3, settingsList.Count);
        Assert.AreEqual(3, settingsList.Count(x => x.SpecialPictures.Length == 4));
        Assert.AreEqual("allTagsUpdatedWithThis",
            settingsList.Find(x => x.Id == "cepheid#1467")
            .SpecialPictures
            .AsEnumerable()
            .ToList()
            .Find(x => x.PictureId == 50)
            .Description);
    }

    [Test]
    public async Task AssignMultiplePortraitsToMultipleTags_PlayersDoNotHave_Success()
    {
        var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
        var portraitRepo = new PortraitRepository(MongoClient);
        var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, portraitRepo);

        List<int> validPortraits = [1, 50, 500, 5000];
        await portraitCommandHandler.AddPortraitDefinitions(CreatePortraitsDefinitionCommand(validPortraits, []));

        var listOfSettings = new List<PersonalSetting>();
        string[] playerTags = ["cepheid#1467", "modmoto#123", "toxi#4321"];

        listOfSettings.Add(new PersonalSetting(playerTags[0]));
        listOfSettings.Add(new PersonalSetting(playerTags[1]));
        listOfSettings.Add(new PersonalSetting(playerTags[2]));

        var portraitIds = new List<int>
        {
            1,
            50,
            500,
            5000
        };

        await personalSettingsRepository.SaveMany(listOfSettings);

        var portraitsCommand = new PortraitsCommand
        {
            Portraits = portraitIds,
            BnetTags = [.. playerTags],
            Tooltip = "Multiple Tags Portrait Test Tooltip"
        };

        await portraitCommandHandler.UpsertSpecialPortraits(portraitsCommand);

        var settingsList = await personalSettingsRepository.LoadMany(playerTags);

        Assert.AreEqual(3, settingsList.Count);
        Assert.AreEqual(4, settingsList.First().SpecialPictures.Length);
        Assert.AreEqual(3, settingsList.Count(x => x.SpecialPictures.Length == 4));
        Assert.AreEqual("Multiple Tags Portrait Test Tooltip", settingsList.Last().SpecialPictures.First().Description);
    }

    [Test]
    public async Task RemoveSpecialPortraits_PlayersHave_Success()
    {
        var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
        var portraitRepo = new PortraitRepository(MongoClient);
        var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, portraitRepo);

        List<int> validPortraits = [5, 50, 500, 5000];
        await portraitCommandHandler.AddPortraitDefinitions(CreatePortraitsDefinitionCommand(validPortraits, []));

        string[] playerTags = ["cepheid#1467"];

        var portraitIds = new List<int>
        {
            5,
            50,
            500,
            5000
        };

        var upsertCommand = new PortraitsCommand
        {
            Portraits = portraitIds,
            BnetTags = [.. playerTags],
            Tooltip = "description"
        };

        var listOfSettings = new List<PersonalSetting>();
        foreach (var tag in playerTags) listOfSettings.Add(new PersonalSetting(tag));
        await personalSettingsRepository.SaveMany(listOfSettings);
        await portraitCommandHandler.UpsertSpecialPortraits(upsertCommand);

        var deleteCommand = new PortraitsCommand
        {
            Portraits = [500],
            BnetTags = [.. playerTags],
            Tooltip = "Multiple Tags Portrait Test Tooltip"
        };

        await portraitCommandHandler.DeleteSpecialPortraits(deleteCommand);
        var settings = await personalSettingsRepository.LoadMany(playerTags);

        Assert.AreEqual(3, settings.First().SpecialPictures.Length);
        CollectionAssert.IsEmpty(settings
            .FindAll(x => x.SpecialPictures
                .AsEnumerable()
                .ToList()
                .Any(x => x.PictureId == 500)));
    }

    [Test]
    public async Task RemoveSpecialPortrait_PlayerDoesNotHave_NoExceptionThrown()
    {
        var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
        var portraitRepo = new PortraitRepository(MongoClient);
        var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, portraitRepo);

        List<int> validPortraits = [5, 50, 500, 5000];
        await portraitCommandHandler.AddPortraitDefinitions(CreatePortraitsDefinitionCommand(validPortraits, []));

        string[] playerTags = ["cepheid#1467"];

        var portraitIds = new List<int>
        {
            5,
            50,
            500,
            5000
        };

        var upsertCommand = new PortraitsCommand
        {
            Portraits = portraitIds,
            BnetTags = playerTags.AsEnumerable().ToList(),
            Tooltip = "description"
        };

        var listOfSettings = new List<PersonalSetting>();
        foreach (var tag in playerTags) listOfSettings.Add(new PersonalSetting(tag));
        await personalSettingsRepository.SaveMany(listOfSettings);
        await portraitCommandHandler.UpsertSpecialPortraits(upsertCommand);

        var deleteCommand = new PortraitsCommand
        {
            Portraits = [100],
            BnetTags = [.. playerTags],
            Tooltip = "this text is irrelevant"
        };

        await portraitCommandHandler.DeleteSpecialPortraits(deleteCommand);
        var settings = await personalSettingsRepository.LoadOrCreate(playerTags[0]);

        Assert.AreEqual(4, settings.SpecialPictures.Length);
    }

    [Test]
    public async Task RemoveSpecialPortrait_PlayerDoesNotHaveSpecialPictures_NoExceptionThrown()
    {
        var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
        var portraitRepo = new PortraitRepository(MongoClient);
        var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, portraitRepo);

        string playerTag = "cepheid#1467";
        var personalSettings = new PersonalSetting(playerTag);
        await personalSettingsRepository.Save(personalSettings);

        var deleteCommand = new PortraitsCommand
        {
            Portraits = [100],
            BnetTags = [playerTag],
            Tooltip = "this text is irrelevant"
        };

        Assert.DoesNotThrowAsync(async () => await portraitCommandHandler.DeleteSpecialPortraits(deleteCommand));
        var settings = await personalSettingsRepository.LoadOrCreate(playerTag);

        Assert.IsEmpty(settings.SpecialPictures);
    }

    [Test]
    public async Task UpdateGroups_PortraitDefinitionExists_Success()
    {
        var settingsRepo = new PersonalSettingsRepository(MongoClient);
        var portraitRepo = new PortraitRepository(MongoClient);
        var portraitCommandHandler = new PortraitCommandHandler(settingsRepo, portraitRepo);
        int[] portraitIds = [1, 2, 3, 4];
        string[] portraitGroups = ["bronze", "silver"];
        var defineCommand = CreatePortraitsDefinitionCommand(portraitIds.ToList(), portraitGroups.ToList());
        await portraitCommandHandler.AddPortraitDefinitions(defineCommand);

        int[] portraitsToUpdate = [1, 4];
        string[] portraitGroupToUpdate = ["gold"];
        var updateCommand = CreatePortraitsDefinitionCommand(portraitsToUpdate.ToList(), portraitGroupToUpdate.ToList());
        await portraitCommandHandler.UpdatePortraitDefinitions(updateCommand);

        var portraits = await portraitCommandHandler.GetPortraitDefinitions();

        var definitionsWithGold = portraits.FindAll(x => x.Groups.Count == 1);
        var definitionsWithBronzeSilver = portraits.FindAll(x => x.Groups.Count == 2);

        Assert.AreEqual(4, portraits.Count);
        Assert.AreEqual(2, definitionsWithGold.Count);
        Assert.AreEqual(2, definitionsWithBronzeSilver.Count);
    }

    [Test]
    public async Task UpdateGroups_PortraitDefinitionDoesntExist_NoError()
    {
        var settingsRepo = new PersonalSettingsRepository(MongoClient);
        var portraitRepo = new PortraitRepository(MongoClient);
        var portraitCommandHandler = new PortraitCommandHandler(settingsRepo, portraitRepo);
        int[] portraitIds = [1, 2, 3, 4];
        string[] portraitGroups = ["bronze", "silver"];
        var defineCommand = CreatePortraitsDefinitionCommand(portraitIds.ToList(), portraitGroups.ToList());
        await portraitCommandHandler.AddPortraitDefinitions(defineCommand);

        int[] portraitsToUpdate = [5];
        string[] portraitGroupToUpdate = ["gold"];
        var updateCommand = CreatePortraitsDefinitionCommand(portraitsToUpdate.ToList(), portraitGroupToUpdate.ToList());
        await portraitCommandHandler.UpdatePortraitDefinitions(updateCommand);

        var portraits = await portraitCommandHandler.GetPortraitDefinitions();

        Assert.AreEqual(4, portraits.Count);
    }

    [Test]
    public async Task UpdatePortrait_OldSchemaWithoutSpecialPicturesField_NoError()
    {
        var settingsRepo = new PersonalSettingsRepository(MongoClient);
        var portraitRepo = new PortraitRepository(MongoClient);
        var portraitCommandHandler = new PortraitCommandHandler(settingsRepo, portraitRepo);

        int[] portraitIds = [1, 2, 3, 4];
        string[] portraitGroups = ["bronze", "silver"];
        var defineCommand = CreatePortraitsDefinitionCommand(portraitIds.ToList(), portraitGroups.ToList());
        await portraitCommandHandler.AddPortraitDefinitions(defineCommand);

        var tag = "cepheid#1467";

        await settingsRepo.Save(new PersonalSetting(tag));
        var settings = await settingsRepo.LoadOrCreate(tag);
        Assert.AreEqual(0, settings.SpecialPictures.Length);
        await settingsRepo.UnsetOne("SpecialPictures", tag);

        var portraitsCommand = new PortraitsCommand();
        portraitsCommand.Portraits.Add(3);
        portraitsCommand.BnetTags.Add(tag);
        portraitsCommand.Tooltip = "testTooltip";

        await portraitCommandHandler.UpsertSpecialPortraits(portraitsCommand);

        settings = await settingsRepo.LoadOrCreate(tag);
        Assert.AreEqual(1, settings.SpecialPictures.Length);
    }

    [Test]
    public async Task UpdateMultiplePortraits_MixOfOldAndNewSchemas_NewSchemaPortraitsAreNotDeleted()
    {
        var settingsRepo = new PersonalSettingsRepository(MongoClient);
        var portraitRepo = new PortraitRepository(MongoClient);
        var portraitCommandHandler = new PortraitCommandHandler(settingsRepo, portraitRepo);

        List<int> portraitIds = [1, 2, 3, 4];
        List<string> portraitGroups = ["gym"];
        var defineCommand = CreatePortraitsDefinitionCommand(portraitIds, portraitGroups);
        await portraitCommandHandler.AddPortraitDefinitions(defineCommand);

        string[] btags = ["Cepheid#1467", "Floss2xDaily#1987"];

        var settingsList = new List<PersonalSetting>
        {
            new("Cepheid#1467"),
            new("Floss2xDaily#1987")
        };
        await settingsRepo.SaveMany(settingsList);

        var portraitsCommand = new PortraitsCommand();
        portraitsCommand.Portraits.Add(1);
        portraitsCommand.Portraits.Add(2);
        portraitsCommand.Portraits.Add(3);
        portraitsCommand.BnetTags.Add(btags[1]);
        portraitsCommand.Tooltip = "floss's portraits";

        await portraitCommandHandler.UpsertSpecialPortraits(portraitsCommand);

        await settingsRepo.UnsetOne("SpecialPictures", btags[0]); // Cepheid#1467 as the old schema

        var flossSettings = await settingsRepo.LoadOrCreate(btags[1]);
        Assert.AreEqual(3, flossSettings.SpecialPictures.Length);

        var portraitsCommand2 = new PortraitsCommand();
        portraitsCommand2.Portraits.Add(4);
        portraitsCommand2.BnetTags = btags.ToList();
        portraitsCommand2.Tooltip = "added portraits";

        await portraitCommandHandler.UpsertSpecialPortraits(portraitsCommand2);

        var cephSettings = await settingsRepo.LoadOrCreate(btags[0]);
        flossSettings = await settingsRepo.LoadOrCreate(btags[1]);
        Assert.AreEqual(4, flossSettings.SpecialPictures.Length);
        Assert.IsNotNull(cephSettings.SpecialPictures);
        Assert.AreEqual(1, cephSettings.SpecialPictures.Length);
    }

    public static PortraitsDefinitionCommand CreatePortraitsDefinitionCommand(List<int> ids, List<string> groups)
    {
        var pdc = new PortraitsDefinitionCommand
        {
            Ids = ids,
            Groups = groups
        };
        return pdc;
    }
}
