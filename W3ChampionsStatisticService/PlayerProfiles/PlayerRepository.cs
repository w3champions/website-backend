﻿using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Domain.CommonValueObjects;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles.GameModeStats;
using W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;
using W3ChampionsStatisticService.PlayerProfiles.RaceStats;
using W3ChampionsStatisticService.PlayerProfiles.GlobalSearch;
using W3ChampionsStatisticService.Ports;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Services;

namespace W3ChampionsStatisticService.PlayerProfiles
{
    public class PlayerRepository : MongoDbRepositoryBase, IPlayerRepository
    {
        private static Dictionary<int, CachedData<List<MmrRank>>> MmrRanksCacheBySeason = new Dictionary<int, CachedData<List<MmrRank>>>();
        private PersonalSettingsProvider _personalSettingsProvider;

        public PlayerRepository(MongoClient mongoClient, PersonalSettingsProvider personalSettingsProvider = null) : base(mongoClient)
        {
          _personalSettingsProvider = personalSettingsProvider;
        }

        public async Task UpsertPlayer(PlayerOverallStats playerOverallStats)
        {
            await Upsert(playerOverallStats, p => p.BattleTag == playerOverallStats.BattleTag);
        }

        public async Task UpsertPlayerOverview(PlayerOverview playerOverview)
        {
            await Upsert(playerOverview);
        }

        public Task<PlayerWinLoss> LoadPlayerWinrate(string playerId, int season)
        {
            return LoadFirst<PlayerWinLoss>($"{season}_{playerId}");
        }

        public async Task<List<PlayerDetails>> LoadPlayersRaceWins(List<string> playerIds)
        {
            var playerRaceWins = CreateCollection<PlayerDetails>(nameof(PlayerOverallStats));
            var personalSettings = CreateCollection<PersonalSetting>();

            return await playerRaceWins
                .Aggregate()
                .Match(x => playerIds.Contains(x.Id))
                .Lookup<PlayerDetails, PersonalSetting, PlayerDetails>(personalSettings,
                    raceWins => raceWins.Id,
                    settings => settings.Id,
                    details => details.PersonalSettings)
                .ToListAsync();
        }

        public Task UpsertWins(List<PlayerWinLoss> winrate)
        {
            return UpsertMany(winrate);
        }

        public async Task<List<int>> LoadMmrs(int season, GateWay gateWay, GameMode gameMode)
        {
            var mongoCollection = CreateCollection<PlayerOverview>();
            var mmrs = await mongoCollection
                .Find(p => p.Season == season &&
                           p.GateWay == gateWay &&
                           p.GameMode == gameMode)
                .Project(p => p.MMR)
                .ToListAsync();
            return mmrs;
        }

        public Task<List<PlayerOverallStats>> SearchForPlayer(string search)
        {
            var lower = search.ToLower();
            return LoadAll<PlayerOverallStats>(p => p.BattleTag.ToLower().Contains(lower));
        }

        public async Task<List<PlayerSearchInfo>> GlobalSearchForPlayer(
          string search,
          string lastObjectId = "",
          int pageSize = 20
        )
        {
            var searchLower = search.ToLower();

            // Fetch entire cache
            var personalSettings = _personalSettingsProvider.GetPersonalSettings();

            // Filter cached personal settings
            var result = personalSettings
              .Where(ps => ps.Id.ToLower().Contains(searchLower)
                 && ps.Id.CompareTo(lastObjectId) > 0)
              .OrderBy(ps => ps.Id)
              .Take(pageSize)
              .Select(ps => new PlayerSearchInfo(ps))
              .ToList();
            var personalSettingIds = result.Select(ps => ps.BattleTag).ToHashSet();

            // Fetch corresponding stats to fill in seasons
            var playerStats = await CreateCollection<PlayerOverallStats>()
              .Find(ps => personalSettingIds.Contains(ps.BattleTag))
              .ToListAsync();
            var playerStatsMap = playerStats.ToDictionary(ps => ps.BattleTag);

            // Populate seasons for each player
            foreach (var player in result) {
              playerStatsMap.TryGetValue(player.BattleTag, out var y);
              if (y != null) {
                player.SetSeasons(y);
              }
            }

            return result;
        }

        public Task<PlayerGameModeStatPerGateway> LoadGameModeStatPerGateway(string id)
        {
            return LoadFirst<PlayerGameModeStatPerGateway>(id);
        }

        public Task UpsertPlayerGameModeStatPerGateway(PlayerGameModeStatPerGateway stat)
        {
            return Upsert(stat);
        }

        public Task<List<PlayerGameModeStatPerGateway>> LoadGameModeStatPerGateway(
            string battleTag,
            GateWay gateWay,
            int season)
        {
            return LoadAll<PlayerGameModeStatPerGateway>(t =>
                t.PlayerIds.Any(player => player.BattleTag==battleTag) &&
                t.GateWay == gateWay &&
                t.Season == season);
        }

        public Task<List<PlayerRaceStatPerGateway>> LoadRaceStatPerGateway(string battleTag, GateWay gateWay, int season)
        {
            return LoadAll<PlayerRaceStatPerGateway>(t => t.BattleTag == battleTag.ToLower() && t.Season == season && t.GateWay == gateWay);
        }

        public Task<PlayerRaceStatPerGateway> LoadRaceStatPerGateway(string battleTag, Race race, GateWay gateWay, int season)
        {
            return LoadFirst<PlayerRaceStatPerGateway>(t => t.BattleTag == battleTag.ToLower() && t.Season == season && t.GateWay == gateWay && t.Race == race);
        }

        public Task UpsertPlayerRaceStat(PlayerRaceStatPerGateway stat)
        {
            return Upsert(stat);
        }

        public Task<PlayerOverallStats> LoadPlayerProfile(string battleTag)
        {
            return LoadFirst<PlayerOverallStats>(p => p.BattleTag == battleTag);
        }

        public Task<PlayerOverview> LoadOverview(string battleTag)
        {
            return LoadFirst<PlayerOverview>(battleTag);
        }

        public float? GetQuantileForPlayer(List<PlayerId> playerIds, GateWay gateWay, GameMode gameMode, Race? race, int season)
        {
            if (!MmrRanksCacheBySeason.ContainsKey(season))
            {
                MmrRanksCacheBySeason[season] = new CachedData<List<MmrRank>>(() => FetchMmrRanks(season).GetAwaiter().GetResult(), TimeSpan.FromMinutes(5));
            }

            var seasonRanks = MmrRanksCacheBySeason[season].GetCachedData();
            var gatewayGameModeRanks = seasonRanks.FirstOrDefault(x => x.Gateway == gateWay && x.GameMode == gameMode);

            var rankKey = GetRankKey(playerIds, gameMode, race);
            if (gatewayGameModeRanks.Ranks.ContainsKey(rankKey))
            {
                var foundRank = gatewayGameModeRanks.Ranks[rankKey];

                var numberOfPlayersAfter = gatewayGameModeRanks.Ranks.Count - foundRank.Rank;
                return numberOfPlayersAfter / (float)gatewayGameModeRanks.Ranks.Count;
            }

            return null;
        }

        public string GetRankKey(List<PlayerId> playerIds, GameMode gameMode, Race? race)
        {
            if (gameMode != GameMode.GM_2v2_AT 
                && gameMode != GameMode.GM_4v4_AT 
                && gameMode != GameMode.GM_LEGION_4v4_x20_AT
                && gameMode != GameMode.GM_DOTA_5ON5_AT)
            {
                if (gameMode == GameMode.GM_1v1)
                {
                    return $"{ playerIds[0].BattleTag}_{ race}";
                }

                return playerIds[0].BattleTag;
            }
            else
            {
                return string.Join("_", playerIds.Select(x => x.BattleTag).OrderBy(x => x));
            }
        }

        private Task<List<PlayerOverview>> LoadOverviews(int season)
        {
            return LoadAll<PlayerOverview>(t => t.Season == season);
        }

        private async Task<List<MmrRank>> FetchMmrRanks(int season)
        {
            var overviews = await LoadOverviews(season);
            List<MmrRank> result = new List<MmrRank>();
            foreach (var overViewsByGateway in overviews.GroupBy(x => x.GateWay))
            {
                foreach (var overViewsByGatewayGameMode in overViewsByGateway.GroupBy(x => x.GameMode))
                {
                    var mmrRanks = new MmrRank
                    {
                        Gateway = overViewsByGateway.Key,
                        GameMode = overViewsByGatewayGameMode.Key,
                    };

                    var orderedByMmr = overViewsByGatewayGameMode.OrderByDescending(x => x.MMR).ToList();

                    for (int i = 0; i < orderedByMmr.Count; i++)
                    {
                        var overView = orderedByMmr[i];
                        var rankKey = GetRankKey(overView.PlayerIds, overViewsByGatewayGameMode.Key, overView.Race);

                        mmrRanks.Ranks[rankKey] = new PlayerMmrRank()
                        {
                            Mmr = overView.MMR,
                            Rank = i,
                        };
                    }

                    result.Add(mmrRanks);
                }
            }

            return result;
        }

        public Task<PlayerMmrRpTimeline> LoadPlayerMmrRpTimeline(string battleTag, Race race, GateWay gateWay, int season, GameMode gameMode)
        {
            return LoadFirst<PlayerMmrRpTimeline>($"{season}_{battleTag}_@{gateWay}_{race}_{gameMode}");
        }

        public Task UpsertPlayerMmrRpTimeline(PlayerMmrRpTimeline mmrRpTimeline)
        {
            return Upsert(mmrRpTimeline);
        }
    }
}
