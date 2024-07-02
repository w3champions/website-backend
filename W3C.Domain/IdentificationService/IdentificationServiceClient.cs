using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using W3C.Contracts.Admin.Permission;

namespace W3C.Domain.IdentificationService;

public class IdentificationServiceClient
{
    /**
     * WEBSITE_BACKEND_TO_ID_SERVICE_SECRET
     * The secret is used to generate SymmetricSecurityKey, it should be be at least 32 bytes to be compatible with
     * HmacSha256Signature. If the secret is shorter you will face error.
     * example: e72dbdaa8b0d89d8fa5eaa2620f31e75186081a555ea14df9202ad6a9f180653
    */
    private readonly HttpClient _httpClient;
    private readonly string _identificationSecret = Environment.GetEnvironmentVariable("WEBSITE_BACKEND_TO_ID_SERVICE_SECRET");
    private static readonly string ServiceApiUrl = Environment.GetEnvironmentVariable("IDENTIFICATION_SERVICE_URI") ?? "http://localhost:8081";
    private string _cachedToken;
    private readonly double _expireTime;
    private DateTime? _tokenExpiryTime;


    public IdentificationServiceClient(IHttpClientFactory httpClientFactory, double expireTimeSeconds = 600)
    {
        _httpClient = httpClientFactory.CreateClient();
        _expireTime = expireTimeSeconds;
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
        _tokenExpiryTime = DateTime.UtcNow.AddSeconds(_expireTime);
        var jwt = new JwtSecurityToken(
            issuer: "w3c-website-backend",
            audience: "w3c-identification-service",
            signingCredentials: new SigningCredentials(secret, SecurityAlgorithms.HmacSha256Signature),
            expires: _tokenExpiryTime,
            claims: new Claim[]
            {
                new("uniq", Guid.NewGuid().ToString()),
            }
        );
        return tokenHandler.WriteToken(jwt);
    }

    private async Task<string> MakePostRequest(string url, object requestBody)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GetToken());
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

    public string GetToken()
    {
        if (string.IsNullOrEmpty(_cachedToken) || DateTime.UtcNow >= _tokenExpiryTime)
        {
            _cachedToken = GenerateToken();
        }
        return _cachedToken;
    }
}
