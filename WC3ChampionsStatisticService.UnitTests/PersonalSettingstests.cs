using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;

namespace WC3ChampionsStatisticService.Tests
{
    [TestFixture]
    public class PersonalSettingsTests : IntegrationTestBase
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

            personalSetting.Players = new List<PlayerOverallStats> { player };
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

            personalSetting.Players = new List<PlayerOverallStats> { player };
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
        public void SetProfilePicture_SpecialAvatar_ButSpecialPicturesNull_DoesNotThrow()
        {
            var player = PlayerOverallStats.Create("peter#123");
            var personalSetting = new PersonalSetting("peter#123") { SpecialPictures = null };
            personalSetting.Players = new List<PlayerOverallStats> { player };
            var expectedProfilePic = ProfilePicture.Default();

            SetPictureCommand cmd = new SetPictureCommand()
            {
                avatarCategory = AvatarCategory.Special,
                pictureId = 2
            };
            Assert.DoesNotThrow(() => personalSetting.SetProfilePicture(cmd));

            Assert.AreEqual(expectedProfilePic.PictureId, personalSetting.ProfilePicture.PictureId);
            Assert.AreEqual(expectedProfilePic.Race, personalSetting.ProfilePicture.Race);
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
                new SetPictureCommand { avatarCategory = AvatarCategory.NE, pictureId = 2 });

            Assert.IsTrue(result);
            var settings = await personalSettingsRepository.Load("modmoto#123");

            Assert.AreEqual(2, settings.ProfilePicture.PictureId);
        }

        [Test]
        public async Task RequestPersonalSettings_SpecialPicturesNull_Load_CorrectlyUpdatedAndReturned()
        {
            // arrange
            var playerRepo = new PlayerRepository(MongoClient);
            var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);

            var player = PlayerOverallStats.Create("cepheid#1467"); 
            var playerSettings = new PersonalSetting("cepheid#1467");
            await playerRepo.UpsertPlayer(player);
            await personalSettingsRepository.Save(playerSettings);
            await personalSettingsRepository.UnsetOne("SpecialPictures", player.BattleTag);

            // act
            var settings = await personalSettingsRepository.Load("cepheid#1467");

            // assert
            Assert.IsNotNull(settings.SpecialPictures);
            Assert.IsEmpty(settings.SpecialPictures);
        }

        [Test]
        public async Task RequestPersonalSettings_SpecialPicturesNull_LoadMany_CorrectlyUpdatedAndReturned()
        {
            // arrange
            var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
            var playerRepo = new PlayerRepository(MongoClient);

            string[] players = { "cepheid#1467", "floss2xdaily#1234", "setcho#4567" };

            foreach (var player in players)
            {
                var stats = PlayerOverallStats.Create(player);
                await playerRepo.UpsertPlayer(stats);
                var newSettings = new PersonalSetting(player);
                await personalSettingsRepository.Save(newSettings);
            }

            await personalSettingsRepository.UnsetOne("SpecialPictures", players[0]);
            await personalSettingsRepository.UnsetOne("SpecialPictures", players[1]);

            // act
            var settings = await personalSettingsRepository.LoadMany(players);

            // assert
            Assert.IsNotNull(settings.Find(x => x.Id == players[0]).SpecialPictures);
            Assert.IsEmpty(settings.Find(x => x.Id == players[0]).SpecialPictures);
            Assert.IsNotNull(settings.Find(x => x.Id == players[1]).SpecialPictures);
            Assert.IsEmpty(settings.Find(x => x.Id == players[1]).SpecialPictures);
            Assert.IsNotNull(settings.Find(x => x.Id == players[2]).SpecialPictures);
            Assert.IsEmpty(settings.Find(x => x.Id == players[2]).SpecialPictures);
        }
    }
}
