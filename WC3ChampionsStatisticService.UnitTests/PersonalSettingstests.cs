using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Admin;
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
            var portraitRepo = new PortraitRepository(MongoClient);
            var portraitCommandHandler = new PortraitCommandHandler(personalSettingsRepository, playerRepo, portraitRepo);

            var player = PlayerOverallStats.Create("modmoto#123");
            for (int i = 0; i < 30; i++)
            {
                player.RecordWin(Race.NE, 1, true);
            }

            await playerRepo.UpsertPlayer(player);

            var result = await portraitCommandHandler.UpdatePicture("modmoto#123",
                new SetPictureCommand {avatarCategory = AvatarCategory.NE, pictureId = 2});

            Assert.IsTrue(result);
            var settings = await personalSettingsRepository.Load("modmoto#123");

            Assert.AreEqual(2, settings.ProfilePicture.PictureId);
        }
    }
}