using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Collections.Generic;
using W3ChampionsStatisticService.Cache;
using W3ChampionsStatisticService.PlayerProfiles.War3InfoPlayerAkas;

// This class is for the Warcraft3.info AKA feature - currently not implemented for any endpoint but left for future requirements.
namespace W3ChampionsStatisticService.Services
{
    public class PlayerAkaProvider
    {
        private static CachedData<List<PlayerAka>> PlayersAkaCache = new CachedData<List<PlayerAka>>(() => FetchAkasSync(), TimeSpan.FromMinutes(60));
        
        public static List<PlayerAka> FetchAkasSync()
        {
            try
            {
               return FetchAkas().GetAwaiter().GetResult();
            }
            catch
            {
                return new List<PlayerAka>();
            }
        }

        public static async Task<List<PlayerAka>> FetchAkas() // list of all Akas requested from W3info
        { 
            var war3infoApiKey = Environment.GetEnvironmentVariable("WAR3_INFO_API_KEY"); // Change this to secret for dev
            var war3infoApiUrl = "https://warcraft3.info/api/v1/aka/battle_net";

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("client-id", war3infoApiKey);

            var response = await httpClient.GetAsync(war3infoApiUrl);
            string data = await response.Content.ReadAsStringAsync();

            var stringData = JsonSerializer.Deserialize<List<PlayerAka>>(data);
            
            return stringData;
        }

        public Player getAkaData(string battleTag) // string should be received all lower-case.
        {
            var akas = PlayersAkaCache.GetCachedData();
            var aka = akas.Find(x => x.aka == battleTag);

            if (aka != null) // Player exists in the aka db
            {
                return aka.player;
            }

            return null;
        }
    }
}