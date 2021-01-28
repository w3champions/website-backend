using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PersonalSettings;

namespace W3ChampionsStatisticService.Ports
{
    public interface IPersonalSettingsRepository
    {
        Task<PersonalSetting> Load(string battletag);
        Task<PersonalSetting> Upvote(string battletag);
        Task<List<PersonalSetting>> LoadSince(DateTimeOffset from);
        Task<List<PersonalSetting>> LoadMany(string[] battletags);
        Task<List<PersonalSetting>> LoadAll();
        Task Save(PersonalSetting setting);
    }
}