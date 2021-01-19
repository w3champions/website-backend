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
                var playerProfile = await _playerRepository.LoadPlayerProfile(battleTag);
                setting = new PersonalSetting(battleTag, new List<PlayerOverallStats> { playerProfile });
            }

            var result = setting.SetProfilePicture(command);

            if (!result) return false;

            await _personalSettingsRepository.Save(setting);
            return true;
        }

        public async Task<bool> UpdateAkaSettings(string battleTag, SetAkaSettingsCommand command)
        {
            var setting = await _personalSettingsRepository.Load(battleTag);
            if (setting == null)
            {
                var playerProfile = await _playerRepository.LoadPlayerProfile(battleTag);
                setting = new PersonalSetting(battleTag, new List<PlayerOverallStats> { playerProfile });
            }

            var result = setting.SetAkaSettings(command);

            await _personalSettingsRepository.Save(setting);
            return true;
        }
    }
}