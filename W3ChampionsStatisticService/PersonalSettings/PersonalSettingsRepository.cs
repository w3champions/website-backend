using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PersonalSettings;

public class PersonalSettingsRepository : MongoDbRepositoryBase, IPersonalSettingsRepository
{
    public PersonalSettingsRepository(MongoClient mongoClient) : base(mongoClient)
    {
    }

    public async Task<PersonalSetting> Load(string battletag)
    {
        var personalSettings = await LoadFirst<PersonalSetting>(battletag);

        if (personalSettings == null)
        {
            personalSettings = new PersonalSetting(battletag);
            await Upsert(personalSettings);
        }

        var playersStatsCollection = CreateCollection<PlayerOverallStats>();
        var playerStats = (await playersStatsCollection.FindAsync(x => x.BattleTag == battletag)).FirstOrDefault();

        if (playerStats != null) {
            personalSettings.RaceWins = playerStats;
        }

        return personalSettings;
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

    public bool SchemaOutdated(PersonalSetting setting)
    {
        if (!setting.ToBsonDocument().Contains("SpecialPictures"))
        {
            return true;
        }
        return false;
    }
}
