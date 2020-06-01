using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Ladder
{
    public class RankQueryHandler
    {
        private readonly IRankRepository _rankRepository;
        private readonly IPlayerRepository _playerRepository;

        public RankQueryHandler(
            IRankRepository rankRepository,
            IPlayerRepository playerRepository)
        {
            _rankRepository = rankRepository;
            _playerRepository = playerRepository;
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
                .ToArray();

            var raceWinRates = (await _playerRepository.LoadPlayersRaceWins(playerIds))
                .ToDictionary(x => x.Id);

            foreach (var rank in ranks)
            {
                foreach (var playerId in rank.Player.PlayerIds)
                {
                    PlayerDetails playerDetails = null;
                    if (raceWinRates.TryGetValue(playerId.BattleTag, out playerDetails))
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
                            SelectedRace = profilePicture?.Race,
                            Country = personalSettings?.Country
                        });
                    }
                }
            }
        }
    }
}
