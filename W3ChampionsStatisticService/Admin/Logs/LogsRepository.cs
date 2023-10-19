using MongoDB.Driver;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using W3C.Domain.Repositories;
using W3ChampionsStatisticService.Ports;
using System.IO;

namespace W3ChampionsStatisticService.Admin.Logs
{
    public class LogsRepository : MongoDbRepositoryBase, ILogsRepository
    {
        private static readonly string MatchmakingApiUrl = Environment.GetEnvironmentVariable("MATCHMAKING_API") ?? "https://matchmaking-service.test.w3champions.com";
        private static readonly string MatchmakingAdminSecret = Environment.GetEnvironmentVariable("ADMIN_SECRET") ?? "300C018C-6321-4BAB-B289-9CB3DB760CBB";
        public LogsRepository(MongoClient mongoClient) : base(mongoClient)
        {
        }

        public async Task<List<string>> GetLogfileNames()
        {
            var httpClient = new HttpClient();
            var response = await httpClient.GetAsync($"{MatchmakingApiUrl}/admin/logs?secret={MatchmakingAdminSecret}");
            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return new List<string> {"Unable to get logfiles."};
            if (response.StatusCode != HttpStatusCode.OK) {
                throw new HttpRequestException(content, null, response.StatusCode);
            }
            var logfileNames = JsonConvert.DeserializeObject<List<string>>(content);
            return logfileNames;
        }

        public async Task<List<string>> GetLogContent(string logfileName)
        {
            var httpClient = new HttpClient();
            var response = await httpClient.GetAsync($"{MatchmakingApiUrl}/admin/logs/{logfileName}?secret={MatchmakingAdminSecret}");
            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content)) return new List<string> {"Unable to get log content."};
            if (response.StatusCode != HttpStatusCode.OK) {
                throw new HttpRequestException(content, null, response.StatusCode);
            }
            var logfileContent = JsonConvert.DeserializeObject<List<string>>(content);
            return logfileContent;
        }

        public async Task<Stream> DownloadLog(string logfileName)
        {
            var httpClient = new HttpClient();
            var response = await httpClient.GetAsync($"{MatchmakingApiUrl}/admin/logs/download/{logfileName}?secret={MatchmakingAdminSecret}");
            var content = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != HttpStatusCode.OK) {
                throw new HttpRequestException(content, null, response.StatusCode);
            }
            Stream stream = await response.Content.ReadAsStreamAsync();
            return stream;
        }
    }
}
