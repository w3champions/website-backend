using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.PadEvents.PadSync;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class BanTests : IntegrationTestBase
    {
        private BanReadmodelRepository _banRepo;

        [SetUp]
        public void SetUp()
        {
            _banRepo = new BanReadmodelRepository(MongoClient);
        }

        [Test]
        public async Task InvitePlayer_ClanNotPresent()
        {
            await _banRepo.UpdateBans(new List<BannedPlayerReadmodel>
            {
                new BannedPlayerReadmodel
                {
                    battleTag = "user#1",
                    endDate = "2020-01-02"
                },
                new BannedPlayerReadmodel
                {
                    battleTag = "user#2",
                    endDate = "2020-01-03"
                }
            });

            var bans = await _banRepo.GetBans();

            Assert.AreEqual("user#2", bans[0].battleTag);
            Assert.AreEqual("user#1", bans[1].battleTag);
            Assert.AreEqual("user#1", (await _banRepo.GetBan("user#1")).battleTag);
        }
    }
}