using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using W3C.Contracts.Admin.Permission;

namespace W3C.Domain.IdentificationService;

public class IdentificationServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly string _identificationSecret = Environment.GetEnvironmentVariable("IDENTIFICATION_SECRET") ?? "testFOO123";
    private static readonly string ServiceApiUrl = Environment.GetEnvironmentVariable("IDENTIFICATION_API") ?? "http://localhost:8081";
    private string _cachedToken;
    private DateTime _tokenExpiryTime;

    public IdentificationServiceClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient();
    }
    public async Task<bool> HasPermission(EPermission ePermission, string battleTag)
    {
        var content = await MakePostRequest($"{ServiceApiUrl}/api/permissions/checkPermission", new
        {
            permission = ePermission,
            battleTag = battleTag
        });
        var deserializeObject = JsonConvert.DeserializeObject<Dictionary<string, bool>>(content);
        return deserializeObject["hasPermission"];
    }

    private string GenerateToken()
    {
        var secret = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_identificationSecret));
        var tokenHandler = new JwtSecurityTokenHandler();
        _tokenExpiryTime = DateTime.UtcNow.AddMinutes(10);
        var jwt = new JwtSecurityToken(
            issuer: "w3c-website-backend",
            audience: "w3c-identification-service",
            signingCredentials: new SigningCredentials(secret, SecurityAlgorithms.HmacSha256Signature),
            expires: _tokenExpiryTime
        );
        return tokenHandler.WriteToken(jwt);
        ;
    }

    private async Task<string> MakePostRequest(string url, object requestBody)
    {
        if (string.IsNullOrEmpty(_cachedToken) || DateTime.UtcNow >= _tokenExpiryTime)
        {
            _cachedToken = GenerateToken();
        }
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _cachedToken);
        var result = await _httpClient.PostAsync(
            url,
            new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json")
        );
        var response = await result.Content.ReadAsStringAsync();
        if (result.IsSuccessStatusCode)
        {
            return response;
        }
        throw new HttpRequestException($"Invalid request from IdentificationService code={result.StatusCode}, response={response}");
    }
}
