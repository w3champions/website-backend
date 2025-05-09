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
using W3ChampionsStatisticService.W3ChampionsStats.PopularHours;
using W3ChampionsStatisticService.W3ChampionsStats.MapsPerSeasons;
using W3ChampionsStatisticService.W3ChampionsStats.OverallRaceAndWinStats;
using W3ChampionsStatisticService.W3ChampionsStats.MatchupLengths;
using System.ComponentModel;

namespace W3ChampionsStatisticService.W3ChampionsStats;

public class W3StatsRepo(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IW3StatsRepo
{
    public Task<List<OverallRaceAndWinStat>> LoadRaceVsRaceStats()
    {
        return LoadAll<OverallRaceAndWinStat>();
    }

    public Task<OverallRaceAndWinStat> LoadRaceVsRaceStat(int mmrRange)
    {
        return LoadFirst(Builders<OverallRaceAndWinStat>.Filter.Eq(m => m.Id, mmrRange));
    }

    public Task Save(OverallRaceAndWinStat stat)
    {
        return Upsert(stat, Builders<OverallRaceAndWinStat>.Filter.Eq(s => s.Id, stat.Id));
    }

    public Task<GamesPerDay> LoadGamesPerDay(DateTime date, GameMode gameMode, GateWay gateway)
    {
        return LoadFirst<GamesPerDay>($"{gateway}_{gameMode}_{date:yyyy-MM-dd}");
    }

    public Task Save(List<GamesPerDay> stat)
    {
        return UpsertMany(stat);
    }

    public Task<GameLengthStat> LoadGameLengths(GameMode mode)
    {
        return LoadFirst(Builders<GameLengthStat>.Filter.Eq(stat => stat.GameMode, mode));
    }

    public Task<List<GameLengthStat>> LoadAllGameLengths()
    {
        return LoadAll<GameLengthStat>();
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
        return await LoadAll(
            Builders<DistinctPlayersPerDay>.Filter.And(
                Builders<DistinctPlayersPerDay>.Filter.Gte(s => s.Date, from),
                Builders<DistinctPlayersPerDay>.Filter.Lte(s => s.Date, to)
            ),
            sortBy: Builders<DistinctPlayersPerDay>.Sort.Descending(s => s.Date)
        );
    }

    public async Task<List<List<GameDayGroup>>> LoadGamesPerDayBetween(
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var stats = await LoadAll(
            Builders<GamesPerDay>.Filter.And(
                Builders<GamesPerDay>.Filter.Gte(s => s.Date, from),
                Builders<GamesPerDay>.Filter.Lte(s => s.Date, to)
            ),
            sortBy: Builders<GamesPerDay>.Sort.Ascending(s => s.Date)
        );

        var americaStats = stats.Where(g => g.GateWay == GateWay.America).ToList();
        var euStats = stats.Where(g => g.GateWay == GateWay.Europe).ToList();
        var allStats = stats.Where(g => g.GateWay == GateWay.Undefined).ToList();
        var gamesPerDays = new[] { allStats, americaStats, euStats };
        return [.. gamesPerDays.Select(s =>
            s.GroupBy(gamesPerDay => gamesPerDay.GameMode)
            .Select(g => new GameDayGroup(g.Key, [.. g]))
            .OrderBy(g => g.GameMode).ToList())];
    }

    public Task<PopularHoursStat> LoadPopularHoursStat(GameMode mode)
    {
        return LoadFirst(Builders<PopularHoursStat>.Filter.Eq(stat => stat.GameMode, mode));
    }

    public Task<List<PopularHoursStat>> LoadAllPopularHoursStat()
    {
        return LoadAll<PopularHoursStat>();
    }

    public Task Save(PopularHoursStat stat)
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
        return LoadAll(Builders<OverallHeroWinRatePerHero>.Filter.Regex(h => h.Id, $"^{heroComboId}"));
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

    public Task Save(MatchupLength matchupLength)
    {
        return Upsert(matchupLength);
    }
    public async Task<MatchupLength> LoadMatchupLengthOrCreate(string race1, string race2, string season)
    {
        var matchupId = MatchupLength.CompoundNormalizedId(race1, race2, season);

        // TODO: Verify this still works
        var stats = await LoadFirst(Builders<MatchupLength>.Filter.Eq(s => s.Id, matchupId));

        if (stats != null)
        {
            return stats;
        }

        return MatchupLength.Create(race1, race2, season);
    }
}
