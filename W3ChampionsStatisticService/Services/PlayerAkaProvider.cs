using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Collections.Generic;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.PlayerProfiles.War3InfoPlayerAkas;
using W3ChampionsStatisticService.PersonalSettings;
using W3C.Domain;
using W3C.Domain.Tracing;
using Serilog;

namespace W3ChampionsStatisticService.Services;

[Trace]
public class PlayerAkaProvider(ICachedDataProvider<List<PlayerAka>> userAccountsCached, IHttpClientFactory httpClientFactory)
{
    private const string War3InfoApiUrl = "https://warcraft3.info/api/v1/aka/battle_net";
    private readonly ICachedDataProvider<List<PlayerAka>> _userAccountsCached = userAccountsCached;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public async Task<List<PlayerAka>> GetAkaReferencesAsync() // list of all Akas requested from W3info
    {
        var war3infoApiKey = Environment.GetEnvironmentVariable("WAR3_INFO_API_KEY"); // Change this to secret for dev

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, War3InfoApiUrl);
            request.Headers.Add("client-id", war3infoApiKey);
            using var response = await _httpClientFactory.CreateClient(HttpClientNames.War3Info).SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("warcraft3.info aka lookup failed with {StatusCode}", response.StatusCode);
                return [];
            }
            string data = await response.Content.ReadAsStringAsync();
            var stringData = JsonSerializer.Deserialize<List<PlayerAka>>(data);
            return stringData;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "warcraft3.info aka lookup failed");
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
