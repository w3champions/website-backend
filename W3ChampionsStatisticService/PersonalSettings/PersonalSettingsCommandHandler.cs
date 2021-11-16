using System.Collections.Generic;
using System.Linq;
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

        public async Task UpsertSpecialPortraits(PortraitsCommand command)
        {
            var settings = await _personalSettingsRepository.LoadMany(command.BnetTags.ToArray());
            
            foreach (var playerSettings in settings)
            {
                var specialPortraitsList = new List<SpecialPicture>(playerSettings.SpecialPictures);
                foreach (var portraitId in command.Portraits)
                {
                    if (!specialPortraitsList.Exists(x => x.PictureId == portraitId))
                    {
                        specialPortraitsList.Add(new SpecialPicture(portraitId, command.Tooltip));
                    }
                }
                playerSettings.UpdateSpecialPictures(specialPortraitsList.ToArray());
            }

            await _personalSettingsRepository.SaveMany(settings);
        }

        public async Task DeleteSpecialPortraits(PortraitsCommand command)
        {
            var settings = await _personalSettingsRepository.LoadMany(command.BnetTags.ToArray());

            foreach (var playerSettings in settings)
            {
                var specialPortraitsList = new List<SpecialPicture>(playerSettings.SpecialPictures);
                var leftoverPortraits = specialPortraitsList.Where(x => !command.Portraits.Contains(x.PictureId));
                playerSettings.UpdateSpecialPictures(leftoverPortraits.ToArray());
            }

            await _personalSettingsRepository.SaveMany(settings);
        }
    }
}