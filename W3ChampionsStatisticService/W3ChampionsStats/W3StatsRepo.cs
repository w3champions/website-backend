using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.ReadModelBase;
using W3ChampionsStatisticService.W3ChampionsStats.DistinctPlayersPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.GameLengths;
using W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats;
using W3ChampionsStatisticService.W3ChampionsStats.HourOfPlay;
using W3ChampionsStatisticService.W3ChampionsStats.RaceAndWinStats;

namespace W3ChampionsStatisticService.W3ChampionsStats
{
    public class W3StatsRepo : MongoDbRepositoryBase, IW3StatsRepo
    {
        public W3StatsRepo(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public Task<Wc3Stats> Load()
        {
            return LoadFirst<Wc3Stats>(s => s.Id == nameof(Wc3Stats));
        }

        public Task Save(Wc3Stats stat)
        {
            return Upsert(stat, s => s.Id == stat.Id);
        }

        public Task<GameDay> LoadGamesPerDay(DateTime date)
        {
            return LoadFirst<GameDay>(s => s.Id == date.Date.ToString("yyyy-MM-dd"));
        }

        public Task Save(GameDay stat)
        {
            return Upsert(stat, s => s.Id == stat.Id);
        }

        public Task<GameLengthStats> LoadGameLengths()
        {
            return LoadFirst<GameLengthStats>(s => s.Id == nameof(GameLengthStats));
        }

        public Task Save(GameLengthStats stat)
        {
            return Upsert(stat, s => s.Id == stat.Id);
        }

        public Task<PlayersOnGameDay> LoadPlayersPerDay(DateTime date)
        {
            return LoadFirst<PlayersOnGameDay>(s => s.Id == date.Date.ToString("yyyy-MM-dd"));
        }

        public Task Save(PlayersOnGameDay stat)
        {
            return Upsert(stat, s => s.Id == stat.Id);
        }

        public async Task<List<PlayersOnGameDay>> LoadPlayersPerDayBetween(DateTimeOffset from, DateTimeOffset to)
        {
            var mongoDatabase = CreateClient();
            var mongoCollection = mongoDatabase.GetCollection<PlayersOnGameDay>(nameof(PlayersOnGameDay));

            var stats = await mongoCollection.Find(s => s.Date >= from && s.Date <= to)
                .SortByDescending(s => s.Date)
                .ToListAsync();

            return stats;
        }

        public async Task<List<GameDay>> LoadGamesPerDayBetween(DateTimeOffset from, DateTimeOffset to)
        {
            var mongoDatabase = CreateClient();
            var mongoCollection = mongoDatabase.GetCollection<GameDay>(nameof(GameDay));

            var stats = await mongoCollection.Find(s => s.Date >= from && s.Date <= to)
                .SortByDescending(s => s.Date)
                .ToListAsync();

            return stats;
        }

        public Task<HourOfPlayStats> LoadHourOfPlay()
        {
            return LoadFirst<HourOfPlayStats>(s => s.Id == nameof(HourOfPlayStats));
        }

        public Task Save(HourOfPlayStats stat)
        {
            return Upsert(stat, s => s.Id == stat.Id);
        }

        public Task<HeroPlayedStat> LoadHeroPlayedStat()
        {
            return LoadFirst<HeroPlayedStat>(s => s.Id == nameof(HeroPlayedStat));
        }

        public Task Save(HeroPlayedStat stat)
        {
            return Upsert(stat, s => s.Id == stat.Id);
        }

        public Task<HeroWinRatePerHero> LoadHeroWinrate(string heroComboIdWinner)
        {
            return LoadFirst<HeroWinRatePerHero>(h => h.Id == heroComboIdWinner);
        }

        public Task Save(HeroWinRatePerHero heroWinrate)
        {
            return Upsert(heroWinrate, h => h.Id == heroWinrate.Id);
        }
    }
}