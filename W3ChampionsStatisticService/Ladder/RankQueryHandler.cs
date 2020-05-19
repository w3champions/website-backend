using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.Ladder
{
    public class RankQueryHandler
    {
        private readonly IRankRepository _rankRepository;
        private readonly IPlayerRepository _playerRepository;
        private readonly IPersonalSettingsRepository _personalSettingsRepository;

        public RankQueryHandler(
            IRankRepository rankRepository,
            IPlayerRepository playerRepository,
            IPersonalSettingsRepository personalSettingsRepository)
        {
            _rankRepository = rankRepository;
            _playerRepository = playerRepository;
            _personalSettingsRepository = personalSettingsRepository;
        }

        public async Task<List<Rank>> LoadPlayersOfLeague(int leagueId, int season, GateWay gateWay, GameMode gameMode)
        {
            var playerRanks = await _rankRepository.LoadPlayersOfLeague(leagueId, season, gateWay, gameMode);

            await PopulateCalculatedRace(playerRanks);
            await PopulatePersonalSettingData(playerRanks);

            return playerRanks;
        }

        private async Task PopulateCalculatedRace(List<Rank> ranks)
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
                    PlayerRaceWins playerRaceWins = null;
                    if (raceWinRates.TryGetValue(playerId.BattleTag, out playerRaceWins))
                    {
                        playerId.CalculatedRace = playerRaceWins.GetMainRace();
                    }
                }
            }
        }

        private async Task PopulatePersonalSettingData(List<Rank> playerRanks)
        {
            var playerIds = playerRanks
                .SelectMany(x => x.Player.PlayerIds)
                .Select(x => x.BattleTag)
                .ToArray();

            var personalSettings = (await _personalSettingsRepository.LoadForPlayers(playerIds))
                .ToDictionary(x => x.Id);

            foreach (var rank in playerRanks)
            {
                foreach (var playerId in rank.Player.PlayerIds)
                {
                    PersonalSetting playerPersonalSetting = null;
                    if (personalSettings.TryGetValue(playerId.BattleTag, out playerPersonalSetting))
                    {
                        if (playerPersonalSetting.ProfilePicture != null)
                        {
                            playerId.SelectedRace = playerPersonalSetting.ProfilePicture.Race;
                            playerId.PictureId = playerPersonalSetting.ProfilePicture.PictureId;
                        }
                    }
                }
            }
        }
    }
}
