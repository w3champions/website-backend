using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PersonalSettingstests : IntegrationTestBase
    {
        [Test]
        public void SetProfilePicture()
        {
            var personalSetting = new PersonalSetting("peter#123");

            var player = PlayerOverallStats.Create("peter#123");
            for (int i = 0; i < 20; i++)
            {
                player.RecordWin(Race.HU, 1, true);
            }

            personalSetting.Players = new List<PlayerOverallStats> {player };
            SetPictureCommand cmd = new SetPictureCommand()
            {
                avatarCategory = AvatarCategory.HU,
                pictureId = 2
            };
            var profilePicture = personalSetting.SetProfilePicture(cmd);

            Assert.IsTrue(profilePicture);
            Assert.AreEqual(AvatarCategory.HU, personalSetting.ProfilePicture.Race);
            Assert.AreEqual(2, personalSetting.ProfilePicture.PictureId);
        }

        [Test]
        public void SetProfilePicture_TooFewWins()
        {
            var personalSetting = new PersonalSetting("peter#123");

            var player = PlayerOverallStats.Create("peter#123");
            for (int i = 0; i < 19; i++)
            {
                player.RecordWin(Race.HU, 1, true);
            }

            personalSetting.Players = new List<PlayerOverallStats> {player };
            SetPictureCommand cmd1 = new SetPictureCommand()
            {
                avatarCategory = AvatarCategory.HU,
                pictureId = 1
            };
            personalSetting.SetProfilePicture(cmd1);

            SetPictureCommand cmd2 = new SetPictureCommand()
            {
                avatarCategory = AvatarCategory.HU,
                pictureId = 2
            };
            var profilePicture = personalSetting.SetProfilePicture(cmd2);

            Assert.IsFalse(profilePicture);
            Assert.AreEqual(AvatarCategory.HU, personalSetting.ProfilePicture.Race);
            Assert.AreEqual(1, personalSetting.ProfilePicture.PictureId);
        }

        [Test]
        public void SetProfilePicture_AllowedPictures()
        {
            var personalSetting = new PersonalSetting("peter#123");

            var player = PlayerOverallStats.Create("peter#123");
            for (int i = 0; i < 20; i++)
            {
                player.RecordWin(Race.HU, 1, true);
            }

            personalSetting.Players = new List<PlayerOverallStats> { player };
            Assert.AreEqual(2, personalSetting.PickablePictures.Single(r => r.Race == Race.HU).Max);
        }

        [Test]
        public async Task RepoLoadWithJoin()
        {
            var settingsRepo = new PersonalSettingsRepository(MongoClient);
            var playerRepo = new PlayerRepository(MongoClient);
            var personalSetting = new PersonalSetting("peter#123");

            var player = PlayerOverallStats.Create("peter#123");
            for (int i = 0; i < 20; i++)
            {
                player.RecordWin(Race.HU, 1, true);
            }

            await playerRepo.UpsertPlayer(player);
            await settingsRepo.Save(personalSetting);

            var loaded = await settingsRepo.Load("peter#123");

            Assert.AreEqual(20, loaded.RaceWins.GetWinsPerRace(Race.HU));
        }

        [Test]
        public async Task RepoLoadWithJoin_NotFoundPlayer()
        {
            var settingsRepo = new PersonalSettingsRepository(MongoClient);
            var playerRepo = new PlayerRepository(MongoClient);
            var personalSetting = new PersonalSetting("peter#123@10");

            var player = PlayerOverallStats.Create("peter#123");

            await playerRepo.UpsertPlayer(player);
            await settingsRepo.Save(personalSetting);

            var loaded = await settingsRepo.Load("peter#123@10");

            Assert.AreEqual(0, loaded.RaceWins.GetWinsPerRace(Race.HU));
        }

        [Test]
        public async Task SetPictureWhenSettingsAreNotThere()
        {
            var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
            var playerRepo = new PlayerRepository(MongoClient);
            var personalSettingsCommandHandler = new PersonalSettingsCommandHandler(personalSettingsRepository, playerRepo);

            var player = PlayerOverallStats.Create("modmoto#123");
            for (int i = 0; i < 30; i++)
            {
                player.RecordWin(Race.NE, 1, true);
            }

            await playerRepo.UpsertPlayer(player);

            var result = await personalSettingsCommandHandler.UpdatePicture("modmoto#123",
                new SetPictureCommand {avatarCategory = AvatarCategory.NE, pictureId = 2});

            Assert.IsTrue(result);
            var settings = await personalSettingsRepository.Load("modmoto#123");

            Assert.AreEqual(2, settings.ProfilePicture.PictureId);
        }

        [Test]
        public async Task UpdateSpecialPicture_Success()
        {
            var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
            var playerRepo = new PlayerRepository(MongoClient);
            var personalSettingsCommandHandler = new PersonalSettingsCommandHandler(personalSettingsRepository, playerRepo);

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
            var personalSettingsCommandHandler = new PersonalSettingsCommandHandler(personalSettingsRepository, playerRepo);

            var playerTag = "cepheid#1467";
            var personalSettings = new PersonalSetting(playerTag);
            await personalSettingsRepository.Save(personalSettings);

            var portraitsCommand = new PortraitsCommand();
            portraitsCommand.Portraits.Add(5);
            portraitsCommand.BnetTags.Add(playerTag);
            portraitsCommand.Tooltip = "testTooltip";

            await personalSettingsCommandHandler.UpsertSpecialPortraits(portraitsCommand);

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
            var personalSettingsCommandHandler = new PersonalSettingsCommandHandler(personalSettingsRepository, playerRepo);

            var playerTag = "cepheid#1467";
            var personalSettings = new PersonalSetting(playerTag);
            personalSettings.SpecialPictures.Append(new SpecialPicture(3, "initialTestDescription"));
            await personalSettingsRepository.Save(personalSettings);

            var portraitsCommand = new PortraitsCommand();
            portraitsCommand.Portraits.Add(3);
            portraitsCommand.BnetTags.Add(playerTag);
            portraitsCommand.Tooltip = "testTooltip";

            await personalSettingsCommandHandler.UpsertSpecialPortraits(portraitsCommand);

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
            var personalSettingsCommandHandler = new PersonalSettingsCommandHandler(personalSettingsRepository, playerRepo);

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

            await personalSettingsCommandHandler.UpsertSpecialPortraits(portraitsCommand);

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
            var personalSettingsCommandHandler = new PersonalSettingsCommandHandler(personalSettingsRepository, playerRepo);

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

            await personalSettingsCommandHandler.UpsertSpecialPortraits(portraitsCommand);

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
            var personalSettingsCommandHandler = new PersonalSettingsCommandHandler(personalSettingsRepository, playerRepo);

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

            await personalSettingsCommandHandler.UpsertSpecialPortraits(portraitsCommand);

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
            var personalSettingsCommandHandler = new PersonalSettingsCommandHandler(personalSettingsRepository, playerRepo);

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
            await personalSettingsCommandHandler.UpsertSpecialPortraits(upsertCommand);

            var deleteCommand = new PortraitsCommand();
            deleteCommand.Portraits = new List<int>();
            deleteCommand.Portraits.Add(500);
            deleteCommand.BnetTags = playerTags.AsEnumerable().ToList();
            deleteCommand.Tooltip = "Multiple Tags Portrait Test Tooltip";

            await personalSettingsCommandHandler.DeleteSpecialPortraits(deleteCommand);
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
            var personalSettingsCommandHandler = new PersonalSettingsCommandHandler(personalSettingsRepository, playerRepo);

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
            await personalSettingsCommandHandler.UpsertSpecialPortraits(upsertCommand);

            var deleteCommand = new PortraitsCommand();
            deleteCommand.Portraits = new List<int>();
            deleteCommand.Portraits.Add(100);
            deleteCommand.BnetTags = playerTags.AsEnumerable().ToList();
            deleteCommand.Tooltip = "this text is irrelevant";

            await personalSettingsCommandHandler.DeleteSpecialPortraits(deleteCommand);
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
    }
}