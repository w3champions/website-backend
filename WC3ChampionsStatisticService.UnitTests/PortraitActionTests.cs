using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.Admin.Portraits;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PortraitActionTests : IntegrationTestBase
    {
        [Test]
        public void UpdateSpecialPicture_Success()
        {
            var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
            var playerRepo = new PlayerRepository(MongoClient);

            var playerTag = "cepheid#1467";
            var personalSettings = new PersonalSetting(playerTag);

            List<SpecialPicture> specialPictures = new List<SpecialPicture>();
            specialPictures.Add(new SpecialPicture(1, "one"));
            personalSettings.UpdateSpecialPictures(specialPictures.ToArray());

            Assert.AreEqual(1, personalSettings.SpecialPictures.Count());
            Assert.AreEqual(specialPictures.First().PictureId, personalSettings.SpecialPictures.First().PictureId);

            specialPictures.RemoveAll(x => x.PictureId == 1);
            personalSettings.UpdateSpecialPictures(specialPictures.ToArray());

            Assert.AreEqual(0, personalSettings.SpecialPictures.Count());
        }

        [Test]
        public async Task AssignOnePortrait_PlayerDoesNotHave_Success()
        {
            var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
            var playerRepo = new PlayerRepository(MongoClient);
            var portraitRepo = new PortraitRepository(MongoClient);
            var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, playerRepo, portraitRepo);

            int[] validPortraits = { 5 };
            await portraitCommandHandler.AddPortraitDefinition(new PortraitsDefinitionCommand(validPortraits.ToList(), new List<string>()));

            var playerTag = "cepheid#1467";
            var personalSettings = new PersonalSetting(playerTag);
            await personalSettingsRepository.Save(personalSettings);

            var portraitsCommand = new PortraitsCommand();
            portraitsCommand.Portraits.Add(5);
            portraitsCommand.BnetTags.Add(playerTag);
            portraitsCommand.Tooltip = "testTooltip";

            await portraitCommandHandler.UpsertSpecialPortraits(portraitsCommand);

            var settings = await personalSettingsRepository.Load(playerTag);

            Assert.AreEqual(1, settings.SpecialPictures.Count());
            Assert.AreEqual(5, settings.SpecialPictures.First().PictureId);
            Assert.AreEqual("testTooltip", settings.SpecialPictures.First().Description);
        }

        [Test]
        public async Task AssignOnePortrait_PlayerAlreadyHas_TooltipUpdated()
        {
            var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
            var playerRepo = new PlayerRepository(MongoClient);
            var portraitRepo = new PortraitRepository(MongoClient);
            var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, playerRepo, portraitRepo);

            int[] validPortraits = { 3 };
            await portraitCommandHandler.AddPortraitDefinition(new PortraitsDefinitionCommand(validPortraits.ToList(), new List<string>()));

            var playerTag = "cepheid#1467";
            var personalSettings = new PersonalSetting(playerTag);
            personalSettings.SpecialPictures.Append(new SpecialPicture(3, "initialTestDescription"));
            await personalSettingsRepository.Save(personalSettings);

            var portraitsCommand = new PortraitsCommand();
            portraitsCommand.Portraits.Add(3);
            portraitsCommand.BnetTags.Add(playerTag);
            portraitsCommand.Tooltip = "testTooltip";

            await portraitCommandHandler.UpsertSpecialPortraits(portraitsCommand);

            var settings = await personalSettingsRepository.Load(playerTag);

            Assert.AreEqual(1, settings.SpecialPictures.Count());
            Assert.AreEqual(3, settings.SpecialPictures.First().PictureId);
            Assert.AreEqual("testTooltip", settings.SpecialPictures.First().Description);
        }

        [Test]
        public async Task AssignOnePortraitToMultipleTags_PlayersDoNotHave_Success()
        {
            var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
            var playerRepo = new PlayerRepository(MongoClient);
            var portraitRepo = new PortraitRepository(MongoClient);
            var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, playerRepo, portraitRepo);
            
            int[] validPortraits = { 8 };
            await portraitCommandHandler.AddPortraitDefinition(new PortraitsDefinitionCommand(validPortraits.ToList(), new List<string>()));

            var listOfSettings = new List<PersonalSetting>();
            string[] playerTags = { "cepheid#1467", "modmoto#123", "toxi#4321" };

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

            Assert.AreEqual(3, settingsList.Count());
            Assert.AreEqual(8, settingsList.First().SpecialPictures.First().PictureId);
            Assert.AreEqual(1, settingsList.First().SpecialPictures.Count());
            Assert.AreEqual(3, settingsList.FindAll(x => x.SpecialPictures.Length == 1).Count());
            Assert.AreEqual("multipleTestTooltip", settingsList.Last().SpecialPictures.First().Description);
        }

        [Test]
        public async Task AssignMultiplePortraitsToMultipleTags_SomePlayersAlreadyHave_UpsertsProcessCorrectly()
        {
            var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
            var playerRepo = new PlayerRepository(MongoClient);
            var portraitRepo = new PortraitRepository(MongoClient);
            var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, playerRepo, portraitRepo);

            int[] validPortraits = { 1 , 50 , 500 , 5000 };
            await portraitCommandHandler.AddPortraitDefinition(new PortraitsDefinitionCommand(validPortraits.ToList(), new List<string>()));

            var listOfSettings = new List<PersonalSetting>();
            string[] playerTags = { "cepheid#1467", "modmoto#123", "toxi#4321" };
            listOfSettings.Add(new PersonalSetting(playerTags[0]));
            listOfSettings.Add(new PersonalSetting(playerTags[1]));
            listOfSettings.Add(new PersonalSetting(playerTags[2]));
            listOfSettings.First().SpecialPictures.Append(new SpecialPicture(50, "fifty"));
            await personalSettingsRepository.SaveMany(listOfSettings);

            var portraitIds = new List<int>();

            portraitIds.Add(1);
            portraitIds.Add(50);
            portraitIds.Add(500);
            portraitIds.Add(5000);

            var portraitsCommand = new PortraitsCommand();
            portraitsCommand.Portraits = portraitIds;
            portraitsCommand.BnetTags = playerTags.AsEnumerable().ToList();
            portraitsCommand.Tooltip = "allTagsUpdatedWithThis";

            await portraitCommandHandler.UpsertSpecialPortraits(portraitsCommand);

            var settingsList = await personalSettingsRepository.LoadMany(playerTags);

            Assert.AreEqual(3, settingsList.Count());
            Assert.AreEqual(3, settingsList.FindAll(x => x.SpecialPictures.Length == 4).Count());
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
            var playerRepo = new PlayerRepository(MongoClient);
            var portraitRepo = new PortraitRepository(MongoClient);
            var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, playerRepo, portraitRepo);

            int[] validPortraits = { 1 , 50 , 500 , 5000 };
            await portraitCommandHandler.AddPortraitDefinition(new PortraitsDefinitionCommand(validPortraits.ToList(), new List<string>()));

            var listOfSettings = new List<PersonalSetting>();
            string[] playerTags = { "cepheid#1467", "modmoto#123", "toxi#4321" };

            listOfSettings.Add(new PersonalSetting(playerTags[0]));
            listOfSettings.Add(new PersonalSetting(playerTags[1]));
            listOfSettings.Add(new PersonalSetting(playerTags[2]));

            var portraitIds = new List<int>();

            portraitIds.Add(1);
            portraitIds.Add(50);
            portraitIds.Add(500);
            portraitIds.Add(5000);

            await personalSettingsRepository.SaveMany(listOfSettings);

            var portraitsCommand = new PortraitsCommand();
            portraitsCommand.Portraits = portraitIds;
            portraitsCommand.BnetTags = playerTags.AsEnumerable().ToList();
            portraitsCommand.Tooltip = "Multiple Tags Portrait Test Tooltip";

            await portraitCommandHandler.UpsertSpecialPortraits(portraitsCommand);

            var settingsList = await personalSettingsRepository.LoadMany(playerTags);

            Assert.AreEqual(3, settingsList.Count());
            Assert.AreEqual(4, settingsList.First().SpecialPictures.Count());
            Assert.AreEqual(3, settingsList.FindAll(x => x.SpecialPictures.Length == 4).Count());
            Assert.AreEqual("Multiple Tags Portrait Test Tooltip", settingsList.Last().SpecialPictures.First().Description);
        }

        [Test]
        public async Task RemoveSpecialPortraits_PlayersHave_Success()
        {
            var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
            var playerRepo = new PlayerRepository(MongoClient);
            var portraitRepo = new PortraitRepository(MongoClient);
            var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, playerRepo, portraitRepo);

            int[] validPortraits = { 5 , 50 , 500 , 5000 };
            await portraitCommandHandler.AddPortraitDefinition(new PortraitsDefinitionCommand(validPortraits.ToList(), new List<string>()));

            string[] playerTags = { "cepheid#1467" };
            
            var portraitIds = new List<int>();
            portraitIds.Add(5);
            portraitIds.Add(50);
            portraitIds.Add(500);
            portraitIds.Add(5000);

            var upsertCommand = new PortraitsCommand();
            upsertCommand.Portraits = portraitIds;
            upsertCommand.BnetTags = playerTags.AsEnumerable().ToList();
            upsertCommand.Tooltip = "description";

            var listOfSettings = new List<PersonalSetting>();
            foreach (var tag in playerTags) listOfSettings.Add(new PersonalSetting(tag));
            await personalSettingsRepository.SaveMany(listOfSettings);
            await portraitCommandHandler.UpsertSpecialPortraits(upsertCommand);

            var deleteCommand = new PortraitsCommand();
            deleteCommand.Portraits = new List<int>();
            deleteCommand.Portraits.Add(500);
            deleteCommand.BnetTags = playerTags.AsEnumerable().ToList();
            deleteCommand.Tooltip = "Multiple Tags Portrait Test Tooltip";

            await portraitCommandHandler.DeleteSpecialPortraits(deleteCommand);
            var settings = await personalSettingsRepository.LoadMany(playerTags);

            Assert.AreEqual(3, settings.First().SpecialPictures.Count());
            CollectionAssert.IsEmpty(settings
                .FindAll(x => x.SpecialPictures
                    .AsEnumerable()
                    .ToList()
                    .FindAll(x => x.PictureId == 500)
                    .Count() > 0));
        }

        [Test]
        public async Task RemoveSpecialPortrait_PlayerDoesNotHave_NoExceptionThrown()
        {
            var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
            var playerRepo = new PlayerRepository(MongoClient);
            var portraitRepo = new PortraitRepository(MongoClient);
            var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, playerRepo, portraitRepo);

            int[] validPortraits = { 5 , 50 , 500 , 5000 };
            await portraitCommandHandler.AddPortraitDefinition(new PortraitsDefinitionCommand(validPortraits.ToList(), new List<string>()));

            string[] playerTags = { "cepheid#1467" };

            var portraitIds = new List<int>();
            portraitIds.Add(5);
            portraitIds.Add(50);
            portraitIds.Add(500);
            portraitIds.Add(5000);

            var upsertCommand = new PortraitsCommand();
            upsertCommand.Portraits = portraitIds;
            upsertCommand.BnetTags = playerTags.AsEnumerable().ToList();
            upsertCommand.Tooltip = "description";

            var listOfSettings = new List<PersonalSetting>();
            foreach (var tag in playerTags) listOfSettings.Add(new PersonalSetting(tag));
            await personalSettingsRepository.SaveMany(listOfSettings);
            await portraitCommandHandler.UpsertSpecialPortraits(upsertCommand);

            var deleteCommand = new PortraitsCommand();
            deleteCommand.Portraits = new List<int>();
            deleteCommand.Portraits.Add(100);
            deleteCommand.BnetTags = playerTags.AsEnumerable().ToList();
            deleteCommand.Tooltip = "this text is irrelevant";

            await portraitCommandHandler.DeleteSpecialPortraits(deleteCommand);
            var settings = await personalSettingsRepository.Load(playerTags[0]);

            Assert.AreEqual(4, settings.SpecialPictures.Count());
        }

        [Test]
        public async Task LoadProfileSince_LastUpdateDateReturnsNothing()
        {
            var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
            var personalSetting = new PersonalSetting("peter#123");
            await personalSettingsRepository.Save(personalSetting);

            var result = await personalSettingsRepository.LoadSince(personalSetting.LastUpdated);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public async Task LoadProfileSince_LastUpdateDateMinusAMsReturnsSomething()
        {
            var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
            var personalSetting = new PersonalSetting("peter#123");
            await personalSettingsRepository.Save(personalSetting);

            var personalSettingLastUpdated = personalSetting.LastUpdated.Subtract(TimeSpan.FromMilliseconds(1));
            var result = await personalSettingsRepository.LoadSince(personalSettingLastUpdated);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("peter#123", result[0].Id);
        }

        [Test]
        public async Task DefineNewPortraits_Success()
        {
            var portraitRepository = new PortraitRepository(MongoClient);
            int[] portraitIds = { 1, 2, 3, 4 };
            string[] portraitGroups = { "bronze", "silver", "gold" };
            await portraitRepository.SaveNewPortraitDefinitions(portraitIds.ToList(), portraitGroups.ToList());

            var portraits = await portraitRepository.LoadPortraitDefinitions();

            Assert.AreEqual(4, portraits.Count);
            Assert.AreEqual(3, portraits.First().Groups.Count());
            Assert.AreEqual(portraits.First().Id, portraitIds.First().ToString());
        }

        [Test]
        public async Task DefineNewPortraits_AlreadyExists_IsNotDuplicated()
        {
            var portraitRepository = new PortraitRepository(MongoClient);
            int[] portraitIds = { 1, 2, 3, 4 };
            List<int> portraitList = portraitIds.ToList();
            await portraitRepository.SaveNewPortraitDefinitions(portraitList);

            portraitList.RemoveAll(x => x > 2);
            await portraitRepository.SaveNewPortraitDefinitions(portraitList);

            var portraits = await portraitRepository.LoadPortraitDefinitions();

            Assert.AreEqual(4, portraits.Count);
        }

        [Test]
        public async Task DeleteDefinedPortrait_Success()
        {
            var portraitRepository = new PortraitRepository(MongoClient);
            int[] portraitIds = { 1, 2, 3, 4 };
            List<int> portraitList = portraitIds.ToList();
            await portraitRepository.SaveNewPortraitDefinitions(portraitList);

            portraitList.RemoveAll(x => x < 3);
            await portraitRepository.DeletePortraitDefinitions(portraitList);

            var portraits = await portraitRepository.LoadPortraitDefinitions();

            Assert.AreEqual(2, portraits.Count);
        }

        [Test]
        public async Task DefineNewPortraits_DuplicateInRequest_IsNotDuplicated()
        {
            var portraitRepository = new PortraitRepository(MongoClient);
            int[] portraitIds = { 1, 1 };
            List<int> portraitList = portraitIds.ToList();
            await portraitRepository.SaveNewPortraitDefinitions(portraitList);

            var portraits = await portraitRepository.LoadPortraitDefinitions();

            Assert.AreEqual(1, portraits.Count);
            Assert.AreEqual(1.ToString(), portraits[0].Id);
        }

        [Test]
        public async Task DeleteDefinedPortrait_DoesNotExist_NoError()
        {
            var portraitRepository = new PortraitRepository(MongoClient);
            int[] portraitIds = { 1, 2, 3, 4 };
            List<int> portraitList = portraitIds.ToList();
            await portraitRepository.SaveNewPortraitDefinitions(portraitList);

            int[] nonExistentPortraitIds = { 10 , 11 };
            List<int> nonExistentPortraitList = nonExistentPortraitIds.ToList();
            await portraitRepository.DeletePortraitDefinitions(nonExistentPortraitList);

            var portraits = await portraitRepository.LoadPortraitDefinitions();

            Assert.AreEqual(4, portraits.Count);
        }

        [Test]
        public async Task UpdateGroups_PortraitDefinitionExists_Success()
        {
            var settingsRepo = new PersonalSettingsRepository(MongoClient);
            var portraitRepo = new PortraitRepository(MongoClient);
            var playeRepo = new PlayerRepository(MongoClient);
            var portraitCommandHandler = new PortraitCommandHandler(settingsRepo, playeRepo, portraitRepo);
            int[] portraitIds = { 1, 2, 3, 4 };
            string[] portraitGroups = { "brozne", "silver" };
            var defineCommand = new PortraitsDefinitionCommand(portraitIds.ToList(), portraitGroups.ToList());
            await portraitCommandHandler.AddPortraitDefinition(defineCommand);

            int[] portraitsToUpdate = { 1, 4 };
            string[] portraitGroupToUpdate = { "gold" };
            var updateCommand = new PortraitsDefinitionCommand(portraitsToUpdate.ToList(), portraitGroupToUpdate.ToList());
            await portraitCommandHandler.UpdatePortraitDefinition(updateCommand);

            var portraits = await portraitCommandHandler.GetPortraitDefinitions();

            var definitionsWithGold = portraits.FindAll(x => x.Groups.Count() == 1);
            var definitionsWithBronzeSilver = portraits.FindAll(x => x.Groups.Count() == 2);

            Assert.AreEqual(4, portraits.Count());
            Assert.AreEqual(2, definitionsWithGold.Count());
            Assert.AreEqual(2, definitionsWithBronzeSilver.Count());
        }

        [Test]
        public async Task UpdateGroups_PortraitDefinitionDoesntExist_NoError()
        {
            var settingsRepo = new PersonalSettingsRepository(MongoClient);
            var portraitRepo = new PortraitRepository(MongoClient);
            var playeRepo = new PlayerRepository(MongoClient);
            var portraitCommandHandler = new PortraitCommandHandler(settingsRepo, playeRepo, portraitRepo);
            int[] portraitIds = { 1, 2, 3, 4 };
            string[] portraitGroups = { "bronze", "silver" };
            var defineCommand = new PortraitsDefinitionCommand(portraitIds.ToList(), portraitGroups.ToList());
            await portraitCommandHandler.AddPortraitDefinition(defineCommand);

            int[] portraitsToUpdate = { 5 };
            string[] portraitGroupToUpdate = { "gold" };
            var updateCommand = new PortraitsDefinitionCommand(portraitsToUpdate.ToList(), portraitGroupToUpdate.ToList());
            await portraitCommandHandler.UpdatePortraitDefinition(updateCommand);

            var portraits = await portraitCommandHandler.GetPortraitDefinitions();

            Assert.AreEqual(4, portraits.Count());
        }
    }
}