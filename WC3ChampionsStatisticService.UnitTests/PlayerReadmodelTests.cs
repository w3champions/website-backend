using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Matches;
using W3ChampionsStatisticService.PlayerProfiles;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PlayerReadmodelTests
    {
        [Test]
        public void Player_RecentProgress()
        {
            var ev = TestDtoHelper.CreateFakeEvent();
            ev.match.players[0].id = "peter#123@10";
            ev.match.players[0].won = true;

            var player = PlayerProfile.Create("peter#12@10", "Peter#12");
            player.UpdateRank(GameMode.GM_1v1, 100, 200, 5, 10, 15);

            Assert.AreEqual(100, player.GameModeStats[0].RankingPointsProgress);
        }
    }
}