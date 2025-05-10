﻿using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.PlayerProfiles;
using W3ChampionsStatisticService.Ports;

namespace W3ChampionsStatisticService.PersonalSettings;

public class PersonalSettingsRepository(MongoClient mongoClient) : MongoDbRepositoryBase(mongoClient), IPersonalSettingsRepository
{
    public async Task<PersonalSetting> Load(string battletag)
    {
        PersonalSetting personalSettings = await LoadFirst<PersonalSetting>(battletag);

        if (personalSettings == null)
        {
            return null;
        }

        PlayerOverallStats playerStats = await LoadFirst(Builders<PlayerOverallStats>.Filter.Eq(x => x.BattleTag, battletag));

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

        PlayerOverallStats playerStats = await LoadFirst(Builders<PlayerOverallStats>.Filter.Eq(x => x.BattleTag, battleTag));

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
        return LoadAll(Builders<PersonalSetting>.Filter.Gt(p => p.LastUpdated, from));
    }

    public async Task<List<PersonalSetting>> LoadMany(string[] battletags)
    {
        return await LoadAll(Builders<PersonalSetting>.Filter.In(p => p.Id, battletags));
    }

    public Task<List<PersonalSetting>> LoadAll()
    {
        return LoadAll<PersonalSetting>();
    }

    public Task Save(PersonalSetting setting)
    {
        setting.RaceWins = null;
        return UpsertTimed(setting, Builders<PersonalSetting>.Filter.Eq(p => p.Id, setting.Id));
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
