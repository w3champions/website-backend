using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.PersonalSettings;

[Trace]
public class PersonalSettingsRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IPersonalSettingsRepository
{
    public async Task<PersonalSetting> Load(string battletag)
    {
        PersonalSetting personalSettings = await LoadFirst<PersonalSetting>(battletag);

        if (personalSettings == null)
        {
            return null;
        }

        PlayerOverallStats playerStats = await CreateCollection<PlayerOverallStats>()
            .Find(x => x.BattleTag == battletag)
            .FirstOrDefaultAsync();

        if (playerStats != null)
        {
            personalSettings.RaceWins = playerStats;
        }

        return personalSettings;
    }

    public async Task<PersonalSetting> LoadOrCreate(string battleTag)
    {
        PersonalSetting personalSettings = await LoadFirst<PersonalSetting>(battleTag);

        if (personalSettings == null)
        {
            personalSettings = new PersonalSetting(battleTag);
            await Upsert(personalSettings);
        }

        PlayerOverallStats playerStats = await CreateCollection<PlayerOverallStats>()
            .Find(x => x.BattleTag == battleTag)
            .FirstOrDefaultAsync();

        if (playerStats != null)
        {
            personalSettings.RaceWins = playerStats;
        }

        return personalSettings;
    }

    public async Task<PersonalSetting> Find(string battletag)
    {
        return await LoadFirst<PersonalSetting>(battletag);
    }

    public Task<List<PersonalSetting>> LoadSince(DateTimeOffset from)
    {
        return LoadSince<PersonalSetting>(from);
    }

    public async Task<List<PersonalSetting>> LoadMany(string[] battletags)
    {
        var settings = CreateCollection<PersonalSetting>();
        var results = await settings
            .Aggregate()
            .Match(p => battletags.Contains(p.Id))
            .ToListAsync();
        return results;
    }

    public Task<List<PersonalSetting>> LoadAll()
    {
        return LoadAll<PersonalSetting>();
    }

    public Task Save(PersonalSetting setting)
    {
        setting.RaceWins = null;
        return UpsertTimed(setting, p => p.Id == setting.Id);
    }

    public Task SaveMany(List<PersonalSetting> settings)
    {
        return UpsertMany(settings);
    }

    public Task UnsetOne(string fieldName, string battleTag)
    {
        return UnsetOne<PersonalSetting>(fieldName, battleTag);
    }

    public async Task UpdateSchema(List<PersonalSetting> settings)
    {
        var updatedSettings = settings;
        List<PersonalSetting> outOfDateDocuments = new();
        foreach (var setting in settings)
        {
            if (!setting.ToBsonDocument().Contains("SpecialPictures"))
            {
                setting.SpecialPictures = Array.Empty<SpecialPicture>();
                outOfDateDocuments.Add(setting);
            }
        }

        await SaveMany(outOfDateDocuments);
        return;
    }

    [NoTrace]
    public bool SchemaOutdated(PersonalSetting setting)
    {
        if (!setting.ToBsonDocument().Contains("SpecialPictures"))
        {
            return true;
        }
        return false;
    }
}
