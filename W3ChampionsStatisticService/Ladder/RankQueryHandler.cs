using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Ladder
{
    public class RankQueryHandler
    {
        private readonly IRankRepository _rankRepository;
        private readonly IPlayerRepository _playerRepository;
        private readonly IClanRepository _clanRepository;

        public RankQueryHandler(
            IRankRepository rankRepository,
            IPlayerRepository playerRepository,
            IClanRepository clanRepository)
        {
            _rankRepository = rankRepository;
            _playerRepository = playerRepository;
            _clanRepository = clanRepository;
        }

        public async Task<List<Rank>> LoadPlayersOfLeague(int leagueId, int season, GateWay gateWay, GameMode gameMode)
        {
            var playerRanks = await _rankRepository.LoadPlayersOfLeague(leagueId, season, gateWay, gameMode);

            await PopulatePlayerInfos(playerRanks);

            return playerRanks;
        }

        public async Task<IEnumerable<CountryRanking>> LoadPlayersOfCountry(string countryCode, int season, GateWay gateWay, GameMode gameMode)
        {
            var playerRanks = await _rankRepository.LoadPlayersOfCountry(countryCode, season, gateWay, gameMode);

            await PopulatePlayerInfos(playerRanks);
            await PopulateLeagueInfo(playerRanks, season, gateWay, gameMode);
            if (gameMode == GameMode.GM_2v2_AT)
            {
                SortTeamsByCountry(playerRanks, countryCode);
            }

            return playerRanks.OrderBy(r => r.LeagueOrder)
                .ThenBy(r => r.LeagueDivision)
                .GroupBy(rank => new { rank.League, rank.LeagueName, rank.LeagueDivision, rank.LeagueOrder }, (league, ranks) => new CountryRanking(league.League, league.LeagueName, league.LeagueDivision, league.LeagueOrder, ranks));
        }

        private async Task PopulatePlayerInfos(List<Rank> ranks)
        {
            var playerIds = ranks
                .SelectMany(x => x.Player.PlayerIds)
                .Select(x => x.BattleTag)
                .ToList();

            var raceWinRates = (await _playerRepository.LoadPlayersRaceWins(playerIds))
                .ToDictionary(x => x.Id);

            var clanMemberships = (await _clanRepository.LoadMemberShips(playerIds))
                .ToDictionary(x => x.Id);

            foreach (var rank in ranks)
            {
                foreach (var playerId in rank.Player.PlayerIds)
                {
                    clanMemberships.TryGetValue(playerId.BattleTag, out var membership);
                    if (raceWinRates.TryGetValue(playerId.BattleTag, out var playerDetails))
                    {
                        if (rank.PlayersInfo == null)
                        {
                            rank.PlayersInfo = new List<PlayerInfo>();
                        }

                        var personalSettings = playerDetails.PersonalSettings?.FirstOrDefault();
                        var profilePicture = personalSettings?.ProfilePicture;

                        rank.PlayersInfo.Add(new PlayerInfo()
                        {
                            BattleTag = playerId.BattleTag,
                            CalculatedRace = playerDetails.GetMainRace(),
                            PictureId = profilePicture?.PictureId,
                            isClassicPicture = profilePicture?.IsClassic ?? false,
                            SelectedRace = profilePicture?.Race,
                            Country = personalSettings?.Country,
                            CountryCode = personalSettings?.CountryCode,
                            Location = personalSettings?.Location,
                            TwitchName = personalSettings?.Twitch,
                            ClanId = membership?.ClanId
                        });
                    }
                }
            }
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
}
