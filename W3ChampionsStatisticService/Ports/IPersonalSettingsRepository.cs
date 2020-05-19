using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.CommonValueObjects;
using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.Ports
{
    public interface IPersonalSettingsRepository
    {
        Task<PersonalSetting> Load(string battletag);
        Task Save(PersonalSetting setting);
        Task<PlayerRaceWins> LoadPlayerRaceWins(string playerRawBattleTag);
        Task UpsertPlayerRaceWin(PlayerRaceWins player);
    }

}