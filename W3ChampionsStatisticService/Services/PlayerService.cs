using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles.GlobalSearch;
using W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Services;

public class PlayerService(IPlayerRepository playerRepository, ICachedDataProvider<List<MmrRank>> mmrCachedDataProvider, PersonalSettingsProvider personalSettingsProvider)
{
    private readonly ICachedDataProvider<List<MmrRank>> _mmrCachedDataProvider = mmrCachedDataProvider;
    private readonly IPlayerRepository _playerRepository = playerRepository;
    private readonly PersonalSettingsProvider _personalSettingsProvider = personalSettingsProvider;

    public async Task<float?> GetQuantileForPlayer(List<PlayerId> playerIds, GateWay gateWay, GameMode gameMode, Race? race, int season)
    {
        var seasonRanks =
            await _mmrCachedDataProvider.GetCachedOrRequestAsync(async () => await FetchMmrRanks(season), season.ToString());
        var gatewayGameModeRanks = seasonRanks.First(x => x.Gateway == gateWay && x.GameMode == gameMode);

        var rankKey = GetRankKey(playerIds, gameMode, race);
        if (gatewayGameModeRanks.Ranks.ContainsKey(rankKey))
        {
            var foundRank = gatewayGameModeRanks.Ranks[rankKey];

            var numberOfPlayersAfter = gatewayGameModeRanks.Ranks.Count - foundRank.Rank;
            return numberOfPlayersAfter / (float)gatewayGameModeRanks.Ranks.Count;
        }

        return null;
    }

    private async Task<List<MmrRank>> FetchMmrRanks(int season)
    {
        var overviews = await _playerRepository.LoadOverviews(season);
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

    public async Task<List<PlayerSearchInfo>> GlobalSearchForPlayer(
        string search,
        string lastRelevanceId = "",
        int pageSize = 20
    )
    {
        var searchLower = search.ToLower();

        // Fetch entire cache
        var personalSettings = await _personalSettingsProvider.GetPersonalSettingsAsync();

        List<PersonalSetting> matchingEntries = personalSettings
            .Where(ps => ps.Id.ToLower().Contains(searchLower))
            .ToList();

        var searchRelevance = new List<PlayerSearchRelevance>();
        foreach (var ps in matchingEntries) {
            int relevance = 9;
            string nameLower = ps.Id.ToLower().Split('#').ElementAtOrDefault(0);
            if (nameLower == null) {
                continue;
            }
            // Exact match
            if (nameLower == searchLower) {
                relevance = 1;
            }
            // Start with
            else if (nameLower.StartsWith(searchLower)) {
                relevance = 2;
            }
            searchRelevance.Add(new PlayerSearchRelevance(ps, relevance));
        }

        List<PlayerSearchInfo> result = searchRelevance
            .OrderBy(x => x.RelevanceId)
            .Where(x => x.RelevanceId.CompareTo(lastRelevanceId) > 0)
            .Take(pageSize)
            .Select(x => new PlayerSearchInfo(x.Player, x.RelevanceId))
            .ToList();

        var personalSettingIds = result.Select(ps => ps.BattleTag).ToHashSet();

        var playerStatsMap = await _playerRepository.GetPlayerBattleTagsAsync(personalSettingIds);

        // Populate seasons for each player
        foreach (var player in result) {
            playerStatsMap.TryGetValue(player.BattleTag, out var y);
            if (y != null) {
                player.SetSeasons(y);
            }
        }

        return result;
    }

    private string GetRankKey(List<PlayerId> playerIds, GameMode gameMode, Race? race)
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
}
