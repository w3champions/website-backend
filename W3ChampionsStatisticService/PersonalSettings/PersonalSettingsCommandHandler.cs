using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class PersonalSettingsCommandHandler
    {
        private readonly IPersonalSettingsRepository _personalSettingsRepository;
        private readonly IPlayerRepository _playerRepository;

        public PersonalSettingsCommandHandler(
            IPersonalSettingsRepository personalSettingsRepository,
            IPlayerRepository playerRepository)
        {
            _personalSettingsRepository = personalSettingsRepository;
            _playerRepository = playerRepository;
        }

        public async Task<bool> UpdatePicture(string battleTag, SetPictureCommand command)
        {
            var setting = await _personalSettingsRepository.Load(battleTag);
            if (setting == null)
            {
                var playerProfile = await _playerRepository.LoadPlayer(battleTag);
                setting = new PersonalSetting(battleTag);
                setting.Players = new List<PlayerProfile> { playerProfile };
            }

            var result = setting.SetProfilePicture(command.Race, command.PictureId);

            if (!result) return false;

            await _personalSettingsRepository.Save(setting);
            return true;
        }
    }
}