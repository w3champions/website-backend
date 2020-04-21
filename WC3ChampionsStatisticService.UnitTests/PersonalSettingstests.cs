using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PersonalSettingstests
    {
        [Test]
        public void SetProfilePicture()
        {
            var personalSetting = new PersonalSetting("peter#123");

            var player = PlayerProfile.Create("peter#123@10", "peter#123");
            for (int i = 0; i < 20; i++)
            {
                player.RecordWin(Race.HU, GameMode.GM_1v1, true, 1000);
            }
            var profilePicture = personalSetting.SetProfilePicture(player, Race.HU, 2);

            Assert.IsTrue(profilePicture);
            Assert.AreEqual(Race.HU, personalSetting.ProfilePicture.Race);
            Assert.AreEqual(2, personalSetting.ProfilePicture.PictureId);
        }

        [Test]
        public void SetProfilePicture_ToFewWins()
        {
            var personalSetting = new PersonalSetting("peter#123");

            var player = PlayerProfile.Create("peter#123@10", "peter#123");
            for (int i = 0; i < 19; i++)
            {
                player.RecordWin(Race.HU, GameMode.GM_1v1, true, 1000);
            }
            personalSetting.SetProfilePicture(player, Race.HU, 1);
            var profilePicture = personalSetting.SetProfilePicture(player, Race.HU, 2);

            Assert.IsFalse(profilePicture);
            Assert.AreEqual(Race.HU, personalSetting.ProfilePicture.Race);
            Assert.AreEqual(1, personalSetting.ProfilePicture.PictureId);
        }
    }
}