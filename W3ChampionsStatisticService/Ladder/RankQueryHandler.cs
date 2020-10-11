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
                            Location = personalSettings?.Location,
                            TwitchName = personalSettings?.Twitch,
                            ClanId = membership?.ClanId
                        });
                    }
                }
            }
        }
    }
}
