using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Admin;
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

        public async Task<bool> UpsertSpecialPortraits(PortraitsCommand command)
        {
            var settings = await _personalSettingsRepository.LoadMany(command.BnetTags.ToArray());
            
            foreach (var playerSettings in settings)
            {
                var newSettings = playerSettings;
                var specialPortraitsList = new List<SpecialPicture>(playerSettings.SpecialPictures);
                foreach (var portrait in command.Portraits)
                {
                    specialPortraitsList.Add(new SpecialPicture(portrait, command.Tooltip));
                }
                newSettings.UpdateSpecialPictures(specialPortraitsList.ToArray());
            }

            await _personalSettingsRepository.SaveMany();
        }

        public async Task<bool> DeleteSpecialPortraits(PortraitsCommand command)
        {
            return true;
        }
    }
}