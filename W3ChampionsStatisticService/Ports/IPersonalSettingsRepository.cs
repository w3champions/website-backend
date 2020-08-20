using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.Ports
{
    public interface IPersonalSettingsRepository
    {
        Task<PersonalSetting> Load(string battletag);
        Task<List<PersonalSetting>> LoadMany(string[] battletags);
        Task Save(PersonalSetting setting);
    }
}