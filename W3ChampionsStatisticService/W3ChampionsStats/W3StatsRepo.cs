using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using W3ChampionsStatisticService.W3ChampionsStats.DistinctPlayersPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.GameLengths;
using W3ChampionsStatisticService.W3ChampionsStats.GamesPerDays;
using W3ChampionsStatisticService.W3ChampionsStats.HeroPlayedStats;
using W3ChampionsStatisticService.W3ChampionsStats.HeroWinrate;
using W3ChampionsStatisticService.W3ChampionsStats.HourOfPlay;
using W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;
using W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats;

namespace W3ChampionsStatisticService.W3ChampionsStats
{
    public class W3StatsRepo : MongoDbRepositoryBase, IW3StatsRepo
    {
        public W3StatsRepo(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public Task<List<OverallRaceAndWinStat>> LoadRaceVsRaceStats()
        {
            return LoadAll<OverallRaceAndWinStat>();
        }

        public Task<OverallRaceAndWinStat> LoadRaceVsRaceStat(int mmrRange)
        {
            return LoadFirst<OverallRaceAndWinStat>(m => m.Id == mmrRange);
        }

        public Task Save(OverallRaceAndWinStat stat)
        {
            return Upsert(stat, s => s.Id == stat.Id);
        }

        public Task<GamesPerDay> LoadGamesPerDay(DateTime date, GameMode gameMode, GateWay gateway)
        {
            return LoadFirst<GamesPerDay>($"{gateway.ToString()}_{gameMode.ToString()}_{date:yyyy-MM-dd}");
        }

        public Task Save(List<GamesPerDay> stat)
        {
            return UpsertMany(stat);
        }

        public Task<GameLengthStat> LoadGameLengths()
        {
            return LoadFirst<GameLengthStat>(nameof(GameLengthStat));
        }

        public Task Save(GameLengthStat stat)
        {
            return Upsert(stat);
        }

        public Task<DistinctPlayersPerDay> LoadPlayersPerDay(DateTime date)
        {
            return LoadFirst<DistinctPlayersPerDay>(date.Date.ToString("yyyy-MM-dd"));
        }

        public Task Save(DistinctPlayersPerDay stat)
        {
            return Upsert(stat);
        }

        public async Task<List<DistinctPlayersPerDay>> LoadPlayersPerDayBetween(DateTimeOffset from, DateTimeOffset to)
        {
            var mongoCollection = CreateCollection<DistinctPlayersPerDay>();

            var stats = await mongoCollection.Find(s => s.Date >= from && s.Date <= to)
                .SortByDescending(s => s.Date)
                .ToListAsync();

            return stats;
        }

        public async Task<List<List<GameDayGroup>>> LoadGamesPerDayBetween(
            DateTimeOffset from,
            DateTimeOffset to)
        {
            var mongoCollection = CreateCollection<GamesPerDay>();

            var stats = await mongoCollection.Find(s =>
                    s.Date >= from
                    && s.Date <= to)
                .SortBy(s => s.Date)
                .ToListAsync();

            var americaStats = stats.Where(g => g.GateWay == GateWay.America).ToList();
            var euStats = stats.Where(g => g.GateWay == GateWay.Europe).ToList();
            var allStats = stats.Where(g => g.GateWay == GateWay.Undefined).ToList();
            var gamesPerDays = new [] { allStats, americaStats, euStats };
            return gamesPerDays.Select(s =>
                s.GroupBy(gamesPerDay => gamesPerDay.GameMode)
                .Select(g => new GameDayGroup(g.Key, g.ToList()))
                .OrderBy(g => g.GameMode).ToList()).ToList();
        }

        public Task<HourOfPlayStat> LoadHourOfPlay()
        {
            return LoadFirst<HourOfPlayStat>(nameof(HourOfPlayStat));
        }

        public Task Save(HourOfPlayStat stat)
        {
            return Upsert(stat);
        }

        public Task<HeroPlayedStat> LoadHeroPlayedStat()
        {
            return LoadFirst<HeroPlayedStat>(nameof(HeroPlayedStat));
        }

        public Task Save(HeroPlayedStat stat)
        {
            return Upsert(stat);
        }

        public Task<OverallHeroWinRatePerHero> LoadHeroWinrate(string heroComboId)
        {
            return LoadFirst<OverallHeroWinRatePerHero>(heroComboId);
        }

        public Task<List<OverallHeroWinRatePerHero>> LoadHeroWinrateLike(string heroComboId)
        {
            return LoadAll<OverallHeroWinRatePerHero>(h => h.Id.StartsWith(heroComboId));
        }

        public Task Save(OverallHeroWinRatePerHero overallHeroWinrate)
        {
            return Upsert(overallHeroWinrate);
        }

        public Task<MapsPerSeason> LoadMapsPerSeason(int matchSeason)
        {
            return LoadFirst<MapsPerSeason>(matchSeason.ToString());
        }

        public Task Save(MapsPerSeason mapsPerSeason)
        {
            return Upsert(mapsPerSeason);
        }

        public Task<List<MapsPerSeason>> LoadMatchesOnMap()
        {
            return LoadAll<MapsPerSeason>();
        }
    }
}
