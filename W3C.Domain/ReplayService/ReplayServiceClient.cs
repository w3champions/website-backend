﻿using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace W3C.Domain.UpdateService
{
    public class ReplayServiceClient
    {
        private static readonly string ReplayServiceUrl = Environment.GetEnvironmentVariable("REPLAY_API") ?? "https://replay-service.test.w3champions.com";
        private static readonly string AdminSecret = Environment.GetEnvironmentVariable("ADMIN_SECRET") ?? "300C018C-6321-4BAB-B289-9CB3DB760CBB";

        private readonly HttpClient _httpClient;
        public ReplayServiceClient(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient();
        }

        public async Task<Stream> GenerateReplay(int gameId)
        {
            var stream = await _httpClient.GetStreamAsync($"{ReplayServiceUrl}/generate/{gameId}?secret={AdminSecret}");
            var memStream = new MemoryStream();
            await stream.CopyToAsync(memStream);
            memStream.Seek(0, SeekOrigin.Begin);
            return memStream;
        }
    }
}
