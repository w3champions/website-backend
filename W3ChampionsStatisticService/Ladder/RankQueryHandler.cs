using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;
using W3ChampionsStatisticService.Clans;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.PlayerProfiles.ProgressionStats;
using W3ChampionsStatisticService.Ports;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Ladder;

[Trace]
public class RankQueryHandler(
    IRankRepository rankRepository,
    IPlayerRepository playerRepository,
    IClanRepository clanRepository,
    ProgressionViewLoader progressionViewLoader,
    IApexLeaderboardRepository apexLeaderboardRepository)
{
    private readonly IRankRepository _rankRepository = rankRepository;
    private readonly IPlayerRepository _playerRepository = playerRepository;
    private readonly IClanRepository _clanRepository = clanRepository;
    private readonly ProgressionViewLoader _progressionViewLoader = progressionViewLoader;
    private readonly IApexLeaderboardRepository _apexLeaderboardRepository = apexLeaderboardRepository;

    public async Task<List<Rank>> SearchPlayersOfLeague(string searchFor, int season, GateWay gateWay, GameMode gameMode)
    {
        var playerRanks = await _rankRepository.SearchPlayerOfLeague(searchFor, season, gateWay, gameMode);
        await PopulateProgression(playerRanks);
        return playerRanks;
    }

    public async Task<List<Rank>> LoadPlayersOfLeague(int leagueId, int season, GateWay gateWay, GameMode gameMode)
    {
        var playerRanks = await _rankRepository.LoadPlayersOfLeague(leagueId, season, gateWay, gameMode);

        await PopulatePlayerInfos(playerRanks);
        await PopulateProgression(playerRanks);

        return playerRanks;
    }

    public async Task<IEnumerable<CountryRanking>> LoadPlayersOfCountry(string countryCode, int season, GateWay gateWay, GameMode gameMode)
    {
        var playerRanks = await _rankRepository.LoadPlayersOfCountry(countryCode, season, gateWay, gameMode);

        await PopulatePlayerInfos(playerRanks);
        await PopulateLeagueInfo(playerRanks, season, gateWay, gameMode);
        await PopulateProgression(playerRanks);
        if (gameMode == GameMode.GM_2v2_AT
            || gameMode == GameMode.GM_4v4_AT
            || gameMode == GameMode.GM_LEGION_4v4_x20_AT
            || gameMode == GameMode.GM_DOTA_5ON5_AT
            || gameMode == GameMode.GM_DS_AT
            || gameMode == GameMode.GM_CF_AT
            || gameMode == GameMode.GM_MINIDOTA_3ON3_AT)
        {
            SortTeamsByCountry(playerRanks, countryCode);
        }

        return playerRanks.OrderBy(r => r.LeagueOrder)
            .ThenBy(r => r.LeagueDivision)
            .GroupBy(rank => new { rank.League, rank.LeagueName, rank.LeagueDivision, rank.LeagueOrder }, (league, ranks) => new CountryRanking(league.League, league.LeagueName, league.LeagueDivision, league.LeagueOrder, ranks));
    }

    private async Task PopulatePlayerInfos(List<Rank> ranks)
    {
        var battleTags = ranks
            .SelectMany(x => x.Player.PlayerIds)
            .Select(x => x.BattleTag)
            .ToList();

        var (raceWinRates, clanMemberships) = await LoadEnrichmentSources(battleTags);

        foreach (var rank in ranks)
        {
            // Rank.PlayersInfo is [BsonIgnore] with a property initializer, so it is never null in
            // practice; the ??= is a defensive guard kept for clarity after the enrichment refactor.
            rank.PlayersInfo ??= new List<PlayerInfo>();
            foreach (var playerId in rank.Player.PlayerIds)
            {
                var info = BuildPlayerInfo(playerId.BattleTag, raceWinRates, clanMemberships);
                if (info != null)
                {
                    rank.PlayersInfo.Add(info);
                }
            }
        }
    }

    private async Task<(Dictionary<string, PlayerDetails> raceWinRates, Dictionary<string, ClanMembership> clanMemberships)>
        LoadEnrichmentSources(List<string> battleTags)
    {
        var raceWinRates = (await _playerRepository.LoadPlayersRaceWins(battleTags))
            .ToDictionary(x => x.Id);

        var clanMemberships = (await _clanRepository.LoadMemberShips(battleTags))
            .ToDictionary(x => x.Id);

        return (raceWinRates, clanMemberships);
    }

    private static PlayerInfo BuildPlayerInfo(
        string battleTag,
        Dictionary<string, PlayerDetails> raceWinRates,
        Dictionary<string, ClanMembership> clanMemberships)
    {
        if (!raceWinRates.TryGetValue(battleTag, out var playerDetails))
        {
            return null;
        }

        clanMemberships.TryGetValue(battleTag, out var membership);

        var personalSettings = playerDetails.PersonalSettings?.FirstOrDefault();
        var profilePicture = personalSettings?.ProfilePicture;

        return new PlayerInfo
        {
            BattleTag = battleTag,
            CalculatedRace = playerDetails.GetMainRace(),
            PictureId = profilePicture?.PictureId,
            isClassicPicture = profilePicture?.IsClassic ?? false,
            SelectedRace = profilePicture?.Race,
            Country = personalSettings?.Country,
            CountryCode = personalSettings?.CountryCode,
            Location = personalSettings?.Location,
            TwitchName = personalSettings?.Twitch,
            ClanId = membership?.ClanId,
        };
    }

    public async Task<ApexLeaderboardResponse> LoadApexLeaderboard(int season, GameMode gameMode)
    {
        var leaderboard = await _apexLeaderboardRepository.LoadApexLeaderboard(season, gameMode);

        if (leaderboard?.Players == null || leaderboard.Players.Count == 0)
        {
            return new ApexLeaderboardResponse
            {
                CutoffApexPoints = leaderboard?.CutoffApexPoints,
                GmCount = leaderboard?.GmCount ?? 0,
                Players = new List<ApexLeaderboardRow>(),
            };
        }

        var battleTags = leaderboard.Players
            .SelectMany(p => p.BattleTags ?? new List<string>())
            .Distinct()
            .ToList();

        var (raceWinRates, clanMemberships) = await LoadEnrichmentSources(battleTags);

        var rows = leaderboard.Players
            .Select(entry => new ApexLeaderboardRow
            {
                ApexPoints = entry.ApexPoints,
                League = entry.League,
                RankNumber = entry.RankNumber,
                PlayersInfo = (entry.BattleTags ?? new List<string>())
                    .Select(tag => BuildPlayerInfo(tag, raceWinRates, clanMemberships))
                    .Where(info => info != null)
                    .ToList(),
            })
            .ToList();

        return new ApexLeaderboardResponse
        {
            CutoffApexPoints = leaderboard.CutoffApexPoints,
            GmCount = leaderboard.GmCount,
            Players = rows,
        };
    }


    private async Task PopulateLeagueInfo(List<Rank> ranks, int season, GateWay gateWay, GameMode gameMode)
    {
        var leagues = (await _rankRepository.LoadLeagueConstellation(season))
            .Where(l => l.Gateway == gateWay && l.GameMode == gameMode)
            .SelectMany(l => l.Leagues)
            .ToDictionary(l => l.Id);

        foreach (var rank in ranks)
        {
            if (leagues.TryGetValue(rank.League, out var league))
            {
                rank.LeagueName = league.Name;
                rank.LeagueDivision = league.Division;
                rank.LeagueOrder = league.Order;
            }
        }
    }

    private async Task PopulateProgression(List<Rank> ranks)
    {
        var views = await _progressionViewLoader.LoadViews(ranks.Select(r => r.Id).ToList());
        foreach (var rank in ranks)
        {
            rank.Progression = views.GetValueOrDefault(rank.Id);
        }
    }

    private void SortTeamsByCountry(List<Rank> ranks, string countryCode)
    {
        ranks.ForEach(pr =>
        {
            pr.PlayersInfo = pr.PlayersInfo.OrderBy(info =>
            {
                string code = (info.CountryCode != null ? info.CountryCode : info.Location);
                return code != countryCode;
            }).ToList();
            pr.Player.PlayerIds = pr.Player.PlayerIds
                .OrderBy(pi => pr.PlayersInfo.Select(info => info.BattleTag)
                .ToList()
                .IndexOf(pi.BattleTag))
                .ToList();
        });
    }
}
