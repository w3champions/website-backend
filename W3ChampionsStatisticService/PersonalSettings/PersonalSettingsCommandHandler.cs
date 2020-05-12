using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PersonalSettings
{
    public class PersonalSettingsCommandHandler
    {
        private readonly IPersonalSettingsRepository _personalSettingsRepository;

        public PersonalSettingsCommandHandler(
            IPersonalSettingsRepository personalSettingsRepository)
        {
            _personalSettingsRepository = personalSettingsRepository;
        }

        public async Task<bool> UpdatePicture(string battleTag, SetPictureCommand command)
        {
            var setting = await _personalSettingsRepository.Load(battleTag);
            if (setting == null)
            {
                var playerProfile = await _personalSettingsRepository.LoadPlayerRaceWins(battleTag);
                setting = new PersonalSetting(battleTag);
                setting.Players = new List<PlayerRaceWins> { playerProfile };
            }

            var result = setting.SetProfilePicture(command.Race, command.PictureId);

            if (!result) return false;

            await _personalSettingsRepository.Save(setting);
            return true;
        }
    }
}