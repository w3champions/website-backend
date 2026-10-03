using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.GameObjects;
using W3C.Contracts.Matchmaking;
using W3C.Domain.CommonValueObjects;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.Ladder;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.PlayerProfiles.GlobalSearch;
using W3ChampionsStatisticService.PlayerProfiles.MmrRankingStats;
using W3ChampionsStatisticService.Ports;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Services;

[Trace]
public class PlayerService(IPlayerRepository playerRepository, ICachedDataProvider<List<MmrRank>> mmrCachedDataProvider, PersonalSettingsProvider personalSettingsProvider, IRankRepository rankRepository)
{
    private readonly ICachedDataProvider<List<MmrRank>> _mmrCachedDataProvider = mmrCachedDataProvider;
    private readonly IPlayerRepository _playerRepository = playerRepository;
    private readonly PersonalSettingsProvider _personalSettingsProvider = personalSettingsProvider;
    private readonly IRankRepository _rankRepository = rankRepository;

    public async Task<float?> GetQuantileForPlayer(List<PlayerId> playerIds, GateWay gateWay, GameMode gameMode, Race? race, int season)
    {
        var seasonRanks =
            await _mmrCachedDataProvider.GetCachedOrRequestAsync(async () => await FetchMmrRanks(season), season.ToString());
        var rankKey = GetRankKey(playerIds, gameMode, race);
        var gatewayGameModeRanks = seasonRanks.FirstOrDefault(x => x.Gateway == gateWay && x.GameMode == gameMode);

        if (gatewayGameModeRanks == null || !gatewayGameModeRanks.Ranks.TryGetValue(rankKey, out PlayerMmrRank foundRank))
        {
            return null;
        }

        var numberOfPlayersAfter = gatewayGameModeRanks.Ranks.Count - foundRank.Rank;
        return numberOfPlayersAfter / (float)gatewayGameModeRanks.Ranks.Count;
    }

    private async Task<List<MmrRank>> FetchMmrRanks(int season)
    {
        var overviews = await _playerRepository.LoadOverviews(season);
        List<MmrRank> result = new();
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

    /// <summary>
    /// The core directory search. Relevance is a function of the search's context: pass a ladder
    /// context (season + gateway + gameMode) and standing on that ladder becomes part of relevance,
    /// because on a ladder a ranked player is the more relevant hit. Omit it and ordering is name
    /// relevance alone.
    /// </summary>
    /// <remarks>
    /// Context has to reach the core rather than being applied by the caller afterwards: ladder
    /// standing is orthogonal to name relevance, so any page cut on name alone samples the ranked
    /// players effectively at random. Measured against the 2022 dump, searching "man" on 1v1/s13/EU
    /// surfaced 4 of its 55 ranked players in the first page of 20; ordering by standing surfaces 20,
    /// with the rest reachable in cursor order.
    /// </remarks>
    public async Task<List<PlayerSearchInfo>> GlobalSearchForPlayer(
        string search,
        string lastRelevanceId = "",
        int pageSize = 20,
        int? season = null,
        GateWay? gateWay = null,
        GameMode? gameMode = null
    )
    {
        // Fetch entire cache
        var personalSettings = await _personalSettingsProvider.GetPersonalSettingsAsync();

        List<PersonalSetting> matchingEntries = personalSettings
            .Where(ps => ps.Id.Contains(search, System.StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        var ladderStanding = await LoadLadderStanding(matchingEntries, season, gateWay, gameMode);

        var searchRelevance = new List<PlayerSearchRelevance>();
        foreach (var ps in matchingEntries)
        {
            string matchingName = ps.Id.Split('#').ElementAtOrDefault(0);
            if (matchingName == null)
            {
                continue;
            }
            searchRelevance.Add(new PlayerSearchRelevance(ps, RelevanceId(ps, matchingName, search, ladderStanding)));
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
        foreach (var player in result)
        {
            playerStatsMap.TryGetValue(player.BattleTag, out var y);
            if (y != null)
            {
                player.SetSeasons(y);
            }
        }

        return result;
    }

    /// <summary>
    /// Where each match stands on the given ladder, keyed by battleTag. Null when no ladder context
    /// was supplied; absent from the map means unranked in that context.
    /// </summary>
    private async Task<Dictionary<string, PlayerLadderStanding>> LoadLadderStanding(
        List<PersonalSetting> matchingEntries,
        int? season,
        GateWay? gateWay,
        GameMode? gameMode)
    {
        if (season == null || gateWay == null || gameMode == null || matchingEntries.Count == 0)
        {
            return null;
        }

        var battleTags = matchingEntries.Select(ps => ps.Id).ToList();
        var ranks = await _rankRepository.LoadLadderStandings(battleTags, season.Value, gateWay.Value, gameMode.Value);

        var standing = new Dictionary<string, PlayerLadderStanding>();
        foreach (var rank in ranks)
        {
            // A team's rank stands for every one of its members, and a 1v1 player holds one rank per
            // race they laddered. Ordering needs one position per player, so keep their best.
            foreach (var battleTag in rank.MemberIds)
            {
                if (string.IsNullOrEmpty(battleTag))
                {
                    continue;
                }
                if (!standing.TryGetValue(battleTag, out var held) || OutranksHeld(rank, held))
                {
                    standing[battleTag] = rank;
                }
            }
        }
        return standing;
    }

    private static bool OutranksHeld(PlayerLadderStanding candidate, PlayerLadderStanding held)
    {
        return candidate.RankingPoints > held.RankingPoints;
    }

    /// <summary>
    /// The sort key, which doubles as the pagination cursor — so it is compared as a string, and its
    /// numeric parts are zero-padded to sort numerically. Ranked entries order by ranking points, the
    /// ladder's own global ordering (see <see cref="PlayerLadderStanding.RankingPoints"/>).
    /// </summary>
    private static string RelevanceId(
        PersonalSetting ps,
        string matchingName,
        string search,
        Dictionary<string, PlayerLadderStanding> ladderStanding)
    {
        int nameRelevance = 9;
        // Exact match
        if (matchingName.Equals(search, System.StringComparison.CurrentCultureIgnoreCase))
        {
            nameRelevance = 1;
        }
        // Start with
        else if (matchingName.StartsWith(search, System.StringComparison.CurrentCultureIgnoreCase))
        {
            nameRelevance = 2;
        }

        if (ladderStanding == null)
        {
            return $"{nameRelevance}_{ps.Id}";
        }

        return ladderStanding.TryGetValue(ps.Id, out var rank)
            ? $"0_{InvertedRankingPoints(rank):D5}_{ps.Id}"
            : $"1_{nameRelevance}_{ps.Id}";
    }

    // The key sorts ascending as a string, so higher points must encode smaller: the complement,
    // scaled to keep the ladder's 0.1-point precision. The clamp pins anything past the 999.99
    // ceiling to the edge of the range; ladder points top out near 60.
    private static int InvertedRankingPoints(PlayerLadderStanding standing)
    {
        return System.Math.Clamp(99999 - (int)System.Math.Round(standing.RankingPoints * 100), 0, 99999);
    }

    [NoTrace]
    private string GetRankKey(List<PlayerId> playerIds, GameMode gameMode, Race? race)
    {
        if (gameMode != GameMode.GM_2v2_AT
            && gameMode != GameMode.GM_4v4_AT
            && gameMode != GameMode.GM_LEGION_4v4_x20_AT
            && gameMode != GameMode.GM_DOTA_5ON5_AT
            && gameMode != GameMode.GM_DS_AT
            && gameMode != GameMode.GM_CF_AT
            && gameMode != GameMode.GM_MINIDOTA_3ON3_AT)
        {
            if (gameMode == GameMode.GM_1v1)
            {
                return $"{playerIds[0].BattleTag}_{race}";
            }

            return playerIds[0].BattleTag;
        }
        else
        {
            return string.Join("_", playerIds.Select(x => x.BattleTag).OrderBy(x => x));
        }
    }
}
