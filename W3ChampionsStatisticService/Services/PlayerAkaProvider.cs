using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Collections.Generic;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.PlayerProfiles.War3InfoPlayerAkas;
using W3ChampionsStatisticService.PersonalSettings;
using W3C.Domain.Tracing;

namespace W3ChampionsStatisticService.Services;

[Trace]
public class PlayerAkaProvider(ICachedDataProvider<List<PlayerAka>> userAccountsCached)
{
    private readonly ICachedDataProvider<List<PlayerAka>> _userAccountsCached = userAccountsCached;

    public static async Task<List<PlayerAka>> GetAkaReferencesAsync() // list of all Akas requested from W3info
    {
        var war3infoApiKey = Environment.GetEnvironmentVariable("WAR3_INFO_API_KEY"); // Change this to secret for dev
        var war3infoApiUrl = "https://warcraft3.info/api/v1/aka/battle_net";

        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("client-id", war3infoApiKey);

        try
        {
            var response = await httpClient.GetAsync(war3infoApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }
            string data = await response.Content.ReadAsStringAsync();
            var stringData = JsonSerializer.Deserialize<List<PlayerAka>>(data);
            return stringData;
        }
        catch
        {
            return [];
        }
    }

    public async Task<Player> GetPlayerAkaDataAsync(string battleTag) // string should be received all lower-case.
    {
        var akas = await _userAccountsCached.GetCachedOrRequestAsync(GetAkaReferencesAsync, null);
        var aka = akas.Find(x => x.aka == battleTag);

        if (aka != null)
        {
            return aka.player;
        }
        return new Player(); // returns an default values if they are not in the database
    }

    public async Task<Player> GetAkaDataByPreferencesAsync(string battletag, PersonalSetting settings)
    {
        var playerAkaData = await GetPlayerAkaDataAsync(battletag.ToLower());

        if (settings != null && settings.AliasSettings != null)  // Strip the data if the player doesn't want it shown.
        {
            var modifiedAka = new Player();

            if (settings.AliasSettings.showAka)
            {
                modifiedAka.name = playerAkaData.name;
                modifiedAka.main_race = playerAkaData.main_race;
                modifiedAka.country = playerAkaData.country;
            }

            if (settings.AliasSettings.showW3info)
            {
                modifiedAka.id = playerAkaData.id;
            }

            if (settings.AliasSettings.showLiquipedia)
            {
                modifiedAka.liquipedia = playerAkaData.liquipedia;
            }

            return modifiedAka;
        }

        return playerAkaData;
    }
}
