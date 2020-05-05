using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using W3ChampionsStatisticService.PadEvents;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerStats;
using W3ChampionsStatisticService.PlayerStats.HeroStats;
using W3ChampionsStatisticService.PlayerStats.RaceOnMapVersusRaceStats;

namespace WC3ChampionsStatisticService.UnitTests
{
    [TestFixture]
    public class PlayerStatsTests : IntegrationTestBase
    {
        [Test]
        public async Task LoadAndSaveMapAndRace()
        {
            var playerRepository = new PlayerStatsRepository(MongoClient);

            var player = RaceOnMapVersusRaceRatio.Create("peter#123");
            await playerRepository.UpsertMapAndRaceStat(player);
            var playerLoaded = await playerRepository.LoadMapAndRaceStat(player.Id);

            Assert.AreEqual(player.Id, playerLoaded.Id);
        }

        [Test]
        public async Task MapWinsAndRace()
        {
            var playerRepository = new PlayerStatsRepository(MongoClient);

            var player = RaceOnMapVersusRaceRatio.Create("peter#123");
            player.AddMapWin(Race.HU, Race.UD, "TM", true);
            player.AddMapWin(Race.HU, Race.OC, "EI", true);
            player.AddMapWin(Race.HU, Race.UD, "TM", false);

            await playerRepository.UpsertMapAndRaceStat(player);
            var playerLoaded = await playerRepository.LoadMapAndRaceStat(player.Id);


            Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.HU, Race.UD, "TM").Wins);
            Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.HU, Race.UD, "TM").Losses);
            Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.HU, Race.OC, "EI").Wins);
        }

        [Test]
        public async Task MapWinsAndRaceRnd()
        {
            var playerRepository = new PlayerStatsRepository(MongoClient);

            var player = RaceOnMapVersusRaceRatio.Create("peter#123");
            player.AddMapWin(Race.RnD, Race.UD, "TM", true);
            player.AddMapWin(Race.HU, Race.RnD, "EI", false);

            await playerRepository.UpsertMapAndRaceStat(player);
            var playerLoaded = await playerRepository.LoadMapAndRaceStat(player.Id);

            Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.RnD, Race.UD, "TM").Wins);
            Assert.AreEqual(1, playerLoaded.GetWinLoss(Race.HU, Race.RnD, "EI").Losses);
        }

        [Test]
        public async Task MapWinsAsTotalRace()
        {
            var playerRepository = new PlayerStatsRepository(MongoClient);

            var player = RaceOnMapVersusRaceRatio.Create("peter#123");
            player.AddMapWin(Race.HU, Race.UD, "TM", true);
            player.AddMapWin(Race.NE, Race.UD, "TM", true);
            player.AddMapWin(Race.OC, Race.UD, "TM", true);

            await playerRepository.UpsertMapAndRaceStat(player);
            var playerLoaded = await playerRepository.LoadMapAndRaceStat(player.Id);

            Assert.AreEqual(3, playerLoaded.GetWinLoss(Race.Total, Race.UD, "TM").Wins);
        }

        [Test]
        public async Task PlayerHeroStats()
        {
            var playerRepository = new PlayerStatsRepository(MongoClient);
            var playerHeroStatsHandler = new PlayerHeroStatsHandler(playerRepository);

            var player = CreatePlayer("player#123", Race.HU, won: true);
            var playerHeroes = CreateHeroes("archmage", "mountainking");

            var enemyUdPlayer = CreatePlayer("enemyUd#567", Race.UD);
            var enemyUdHeroes = CreateHeroes("deathknight", "lich");

            // Match 1 Hu vs UD
            MatchFinishedEvent match1 = CreateMatchEvent(player, playerHeroes, enemyUdPlayer, enemyUdHeroes);
            await playerHeroStatsHandler.Update(match1);

            // Match 2 Hu vs NE
            var enemyNePlayer = CreatePlayer("enemyNe#89", Race.NE);
            var enemyNeHeroes = CreateHeroes("deamonhunter");
            MatchFinishedEvent match2 = CreateMatchEvent(player, playerHeroes, enemyNePlayer, enemyNeHeroes);
            await playerHeroStatsHandler.Update(match2);

            var playerHeroStats = await playerRepository.LoadHeroStat(player.id);
            var enemyUdHeroStats = await playerRepository.LoadHeroStat(enemyUdPlayer.id);
            var enemyNeHeroStats = await playerRepository.LoadHeroStat(enemyNePlayer.id);

            // *** Player hero stats
            Assert.AreEqual(player.id, playerHeroStats.Id);
            Assert.AreEqual(2, playerHeroStats.HeroStatsItemList.Count);

            // Archmage Stats
            var playerAmStats = GetHeroStatsForRaceAndMap(playerHeroStats, "archmage", Race.HU, "Overall");

            var playerAmVsUd = playerAmStats.WinLosses.Single(x => x.Race == Race.UD);
            Assert.AreEqual(1, playerAmVsUd.Wins);
            Assert.AreEqual(0, playerAmVsUd.Losses);

            var playerAmVsNe = playerAmStats.WinLosses.Single(x => x.Race == Race.UD);
            Assert.AreEqual(1, playerAmVsNe.Wins);
            Assert.AreEqual(0, playerAmVsNe.Losses);

            // Mountain king Stats
            var playerMKStats = GetHeroStatsForRaceAndMap(playerHeroStats, "mountainking", Race.HU, "Overall");

            var playerMKVsUd = playerMKStats.WinLosses.Single(x => x.Race == Race.UD);
            Assert.AreEqual(1, playerMKVsUd.Wins);
            Assert.AreEqual(0, playerMKVsUd.Losses);

            var playerMKVsNe = playerMKStats.WinLosses.Single(x => x.Race == Race.UD);
            Assert.AreEqual(1, playerMKVsNe.Wins);
            Assert.AreEqual(0, playerMKVsNe.Losses);

            // *** Enemy UD player hero stats
            Assert.AreEqual(enemyUdPlayer.id, enemyUdHeroStats.Id);
            Assert.AreEqual(2, enemyUdHeroStats.HeroStatsItemList.Count);

            // Enemy UD player DK stats
            var enemyUdDkStats = GetHeroStatsForRaceAndMap(enemyUdHeroStats, "deathknight", Race.UD, "Overall");

            var enemyUdDkVsHu = enemyUdDkStats.WinLosses.Single(x => x.Race == Race.HU);
            Assert.AreEqual(0, enemyUdDkVsHu.Wins);
            Assert.AreEqual(1, enemyUdDkVsHu.Losses);

            // Enemy UD player Lich stats
            var enemyUdLichStats = GetHeroStatsForRaceAndMap(enemyUdHeroStats, "lich", Race.UD, "Overall");

            var enemyUdLichVsHu = enemyUdLichStats.WinLosses.Single(x => x.Race == Race.HU);
            Assert.AreEqual(0, enemyUdLichVsHu.Wins);
            Assert.AreEqual(1, enemyUdLichVsHu.Losses);

            // *** Enemy NE player hero stats
            Assert.AreEqual(enemyNePlayer.id, enemyNeHeroStats.Id);
            Assert.AreEqual(1, enemyNeHeroStats.HeroStatsItemList.Count);

            // Enemy NE player DH stats
            var enemyNeDhStats = GetHeroStatsForRaceAndMap(enemyNeHeroStats, "deamonhunter", Race.NE, "Overall");

            var enemyNeDhVsHu = enemyNeDhStats.WinLosses.Single(x => x.Race == Race.HU);
            Assert.AreEqual(0, enemyNeDhVsHu.Wins);
            Assert.AreEqual(1, enemyNeDhVsHu.Losses);
        }

        private static WinLossesPerMap GetHeroStatsForRaceAndMap(PlayerHeroStats playerHeroStats, string heroId, Race race, string mapName)
        {
            var heroStats = playerHeroStats.HeroStatsItemList.Single(x => x.HeroId == heroId).Stats;
            var heroRaceStats = heroStats.Single(x => x.Race == race);
            var heroRaceStatsOnMap = heroRaceStats.WinLossesOnMap.Single(x => x.Map == mapName);

            return heroRaceStatsOnMap;
        }

        private static MatchFinishedEvent CreateMatchEvent(
            PlayerMMrChange player,
            List<Hero> playerHeroes,
            PlayerMMrChange enemyPlayer,
            List<Hero> enemyHeroes)
        {
            var matchFinishedEvent = TestDtoHelper.CreateFakeEvent();

            matchFinishedEvent.match.players[0] = player;
            matchFinishedEvent.result.players[0].heroes = playerHeroes;

            matchFinishedEvent.match.players[1] = enemyPlayer;
            matchFinishedEvent.result.players[1].heroes = enemyHeroes;


            return matchFinishedEvent;
        }

        private static List<Hero> CreateHeroes(params string[] heroes)
        {
            return heroes
                .Select(x => new Hero() { icon = x })
                .ToList();
        }

        private static PlayerMMrChange CreatePlayer(string playerId, Race race, bool won = false)
        {
           return new PlayerMMrChange()
            {
                id = playerId,
                race = (int)race,
                won = won
           };
        }
    }
}