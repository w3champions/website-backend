using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PersonalSettings;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PersonalSettingstests : IntegrationTestBase
    {
        [Test]
        public void SetProfilePicture()
        {
            var personalSetting = new PersonalSetting("peter#123");

            var player = PlayerRaceWins.Create("peter#123");
            for (int i = 0; i < 20; i++)
            {
                player.RecordWin(Race.HU, true);
            }

            personalSetting.Players = new List<PlayerRaceWins> {player };
            var profilePicture = personalSetting.SetProfilePicture(Race.HU, 2);

            Assert.IsTrue(profilePicture);
            Assert.AreEqual(Race.HU, personalSetting.ProfilePicture.Race);
            Assert.AreEqual(2, personalSetting.ProfilePicture.PictureId);
        }

        [Test]
        public void SetProfilePicture_ToFewWins()
        {
            var personalSetting = new PersonalSetting("peter#123");

            var player = PlayerRaceWins.Create("peter#123");
            for (int i = 0; i < 19; i++)
            {
                player.RecordWin(Race.HU, true);
            }

            personalSetting.Players = new List<PlayerRaceWins> {player };
            personalSetting.SetProfilePicture(Race.HU, 1);
            var profilePicture = personalSetting.SetProfilePicture(Race.HU, 2);

            Assert.IsFalse(profilePicture);
            Assert.AreEqual(Race.HU, personalSetting.ProfilePicture.Race);
            Assert.AreEqual(1, personalSetting.ProfilePicture.PictureId);
        }

        [Test]
        public void SetProfilePicture_AllowedPictures()
        {
            var personalSetting = new PersonalSetting("peter#123");

            var player = PlayerRaceWins.Create("peter#123");
            for (int i = 0; i < 20; i++)
            {
                player.RecordWin(Race.HU, true);
            }

            personalSetting.Players = new List<PlayerRaceWins> { player };
            Assert.AreEqual(2, personalSetting.PickablePictures.Single(r => r.Race == Race.HU).Max);
        }

        [Test]
        public async Task RepoLoadWithJoin()
        {
            var settingsRepo = new PersonalSettingsRepository(MongoClient);

            var personalSetting = new PersonalSetting("peter#123");

            var player = PlayerRaceWins.Create("peter#123");
            for (int i = 0; i < 20; i++)
            {
                player.RecordWin(Race.HU,  true);
            }

            await settingsRepo.UpsertPlayerRaceWin(player);
            await settingsRepo.Save(personalSetting);

            var loaded = await settingsRepo.Load("peter#123");

            Assert.AreEqual(20, loaded.Player.GetWinsPerRace(Race.HU));
        }

        [Test]
        public async Task RepoLoadWithJoin_NotFoundPlayer()
        {
            var settingsRepo = new PersonalSettingsRepository(MongoClient);

            var personalSetting = new PersonalSetting("peter#123@10");

            var player = PlayerRaceWins.Create("peter#123");

            await settingsRepo.UpsertPlayerRaceWin(player);
            await settingsRepo.Save(personalSetting);

            var loaded = await settingsRepo.Load("peter#123@10");

            Assert.AreEqual(0, loaded.Player.GetWinsPerRace(Race.HU));
        }

        [Test]
        public async Task SetPictureWhenSettingsAreNotThere()
        {
            var personalSettingsRepository = new PersonalSettingsRepository(MongoClient);
            var personalSettingsCommandHandler = new PersonalSettingsCommandHandler(personalSettingsRepository);

            var player = PlayerRaceWins.Create("modmoto#123");
            for (int i = 0; i < 30; i++)
            {
                player.RecordWin(Race.NE, true);
            }

            await personalSettingsRepository.UpsertPlayerRaceWin(player);

            var result = await personalSettingsCommandHandler.UpdatePicture("modmoto#123",
                new SetPictureCommand {Race = Race.NE, PictureId = 2});

            Assert.IsTrue(result);
            var settings = await personalSettingsRepository.Load("modmoto#123");

            Assert.AreEqual(2, settings.ProfilePicture.PictureId);
        }
    }
}