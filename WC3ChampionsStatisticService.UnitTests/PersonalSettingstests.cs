using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
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

            var player = PlayerProfile.Create("peter#123");
            for (int i = 0; i < 20; i++)
            {
                player.RecordWin(Race.HU, GameMode.GM_1v1, true);
            }

            personalSetting.Players = new List<PlayerProfile> {player };
            var profilePicture = personalSetting.SetProfilePicture(Race.HU, 2);

            Assert.IsTrue(profilePicture);
            Assert.AreEqual(Race.HU, personalSetting.ProfilePicture.Race);
            Assert.AreEqual(2, personalSetting.ProfilePicture.PictureId);
        }

        [Test]
        public void SetProfilePicture_ToFewWins()
        {
            var personalSetting = new PersonalSetting("peter#123");

            var player = PlayerProfile.Create("peter#123");
            for (int i = 0; i < 19; i++)
            {
                player.RecordWin(Race.HU, GameMode.GM_1v1, true);
            }

            personalSetting.Players = new List<PlayerProfile> {player };
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

            var player = PlayerProfile.Create("peter#123");
            for (int i = 0; i < 20; i++)
            {
                player.RecordWin(Race.HU, GameMode.GM_1v1, true);
            }

            personalSetting.Players = new List<PlayerProfile> { player };
            Assert.AreEqual(2, personalSetting.PickablePictures.Single(r => r.Race == Race.HU).Max);
        }

        [Test]
        public async Task RepoLoadWithJoin()
        {
            var playerRepository = new PlayerRepository(MongoClient);
            var settingsRepo = new PersonalSettingsRepository(MongoClient);


            var personalSetting = new PersonalSetting("peter#123");

            var player = PlayerProfile.Create("peter#123");
            for (int i = 0; i < 20; i++)
            {
                player.RecordWin(Race.HU, GameMode.GM_1v1, true);
            }

            await playerRepository.UpsertPlayer(player);
            await settingsRepo.Save(personalSetting);

            var loaded = await settingsRepo.Load("peter#123");

            Assert.AreEqual(20, loaded.Player.GetWinsPerRace(Race.HU));
        }

        [Test]
        public async Task RepoLoadWithJoin_NotFoundPlayer()
        {
            var playerRepository = new PlayerRepository(MongoClient);
            var settingsRepo = new PersonalSettingsRepository(MongoClient);

            var personalSetting = new PersonalSetting("peter#123@10");

            var player = PlayerProfile.Create("peter#123");

            await playerRepository.UpsertPlayer(player);
            await settingsRepo.Save(personalSetting);

            var loaded = await settingsRepo.Load("peter#123@10");

            Assert.AreEqual(0, loaded.Player.GetWinsPerRace(Race.HU));
        }
    }
}