﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3ChampionsStatisticService.PersonalSettings;
using W3ChampionsStatisticService.Ports;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Rewards.Portraits;

[Trace]
public class PortraitCommandHandler(
    IPersonalSettingsRepository personalSettingsRepository,
    IPortraitRepository portraitRepository)
{
    private readonly IPersonalSettingsRepository _personalSettingsRepository = personalSettingsRepository;
    private readonly IPortraitRepository _portraitRepository = portraitRepository;

    public async Task<bool> UpdatePicture(string battleTag, SetPictureCommand command)
    {
        var setting = await _personalSettingsRepository.LoadOrCreate(battleTag);

        var result = setting.SetProfilePicture(command);

        if (!result) return false;

        await _personalSettingsRepository.Save(setting);
        return true;
    }

    public async Task UpsertSpecialPortraits(PortraitsCommand command)
    {
        var settings = await _personalSettingsRepository.LoadMany(command.BnetTags.ToArray());
        await UpdateSchema(settings);
        settings = await _personalSettingsRepository.LoadMany(command.BnetTags.ToArray());

        var validPortraits = await _portraitRepository.LoadPortraitDefinitions();

        foreach (var playerSettings in settings)
        {
            var specialPortraitsList = playerSettings.SpecialPictures != null ? new List<SpecialPicture>(playerSettings.SpecialPictures) : [];
            foreach (var portraitId in command.Portraits)
            {
                if (validPortraits.Any(x => x.Id == portraitId.ToString()))
                {
                    if (specialPortraitsList.Exists(x => x.PictureId == portraitId))
                        specialPortraitsList.RemoveAll(x => x.PictureId == portraitId);
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
            if (playerSettings.SpecialPictures != null)
            {
                var existingPortraits = new List<SpecialPicture>(playerSettings.SpecialPictures);
                existingPortraits.RemoveAll(x => command.Portraits.Contains(x.PictureId));
                playerSettings.UpdateSpecialPictures(existingPortraits.ToArray());
            }
        }

        await _personalSettingsRepository.SaveMany(settings);
    }

    public async Task<List<PortraitDefinition>> GetPortraitDefinitions()
    {
        return await _portraitRepository.LoadPortraitDefinitions();
    }

    public async Task AddPortraitDefinitions(PortraitsDefinitionCommand command)
    {
        await _portraitRepository.SaveNewPortraitDefinitions(command.Ids, command.Groups);
    }

    public async Task RemovePortraitDefinitions(PortraitsDefinitionCommand command)
    {
        await _portraitRepository.DeletePortraitDefinitions(command.Ids);
    }

    public async Task UpdatePortraitDefinitions(PortraitsDefinitionCommand command)
    {
        await _portraitRepository.UpdatePortraitDefinition(command.Ids, command.Groups);
    }

    private async Task UpdateSchema(List<PersonalSetting> settings)
    {
        await _personalSettingsRepository.UpdateSchema(settings);
    }

    public async Task<List<PortraitGroup>> GetPortraitGroups()
    {
        return await _portraitRepository.LoadDistinctPortraitGroups();
    }
}
