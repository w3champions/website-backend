using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using W3C.Domain.Rewards.Repositories;
using System.Net.Http.Headers;

namespace W3ChampionsStatisticService.Rewards.Services;

public class PatreonOAuthService
{
    private readonly HttpClient _httpClient;
    private readonly IPatreonAccountLinkRepository _patreonLinkRepository;
    private readonly ILogger<PatreonOAuthService> _logger;
    
    private const string PatreonTokenUrl = "https://www.patreon.com/api/oauth2/token";
    private const string PatreonUserUrl = "https://www.patreon.com/api/oauth2/v2/identity";
    
    private readonly string _clientId;
    private readonly string _clientSecret;

    public PatreonOAuthService(
        HttpClient httpClient, 
        IPatreonAccountLinkRepository patreonLinkRepository,
        ILogger<PatreonOAuthService> logger)
    {
        _httpClient = httpClient;
        _patreonLinkRepository = patreonLinkRepository;
        _logger = logger;
        
        _clientId = Environment.GetEnvironmentVariable("PATREON_CLIENT_ID");
        _clientSecret = Environment.GetEnvironmentVariable("PATREON_CLIENT_SECRET");

        // Patreon requires a User-Agent header; also prefer JSON responses
        try
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("W3Champions.com/WebsiteBackend/RewardsService");
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
        catch (Exception)
        {
            // Ignore header parsing issues; requests will still proceed
        }
    }

    /// <summary>
    /// Complete OAuth flow by exchanging code for access token and creating account link
    /// </summary>
    public async Task<PatreonOAuthResult> CompleteOAuthFlow(string code, string state, string redirectUri, string battleTag)
    {
        if (string.IsNullOrEmpty(code))
            throw new ArgumentException("Authorization code is required", nameof(code));
        
        if (string.IsNullOrEmpty(state))
            throw new ArgumentException("State parameter is required", nameof(state));
        
        if (string.IsNullOrEmpty(battleTag))
            throw new ArgumentException("BattleTag is required", nameof(battleTag));

        try
        {
            // Step 1: Exchange authorization code for access token
            var tokenResponse = await ExchangeCodeForToken(code, redirectUri);
            
            if (tokenResponse == null)
            {
                return new PatreonOAuthResult
                {
                    Success = false,
                    ErrorMessage = "Failed to exchange authorization code for access token"
                };
            }

            if (string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                _logger.LogError("Patreon token exchange returned no access_token");
                return new PatreonOAuthResult
                {
                    Success = false,
                    ErrorMessage = "Patreon did not return an access token"
                };
            }

            // Step 2: Get Patreon user profile
            var userProfile = await GetPatreonUserProfile(tokenResponse.AccessToken);
            
            if (userProfile == null)
            {
                return new PatreonOAuthResult
                {
                    Success = false,
                    ErrorMessage = "Failed to retrieve Patreon user profile"
                };
            }

            // Step 3: Create or update account link
            var accountLink = await _patreonLinkRepository.UpsertLink(battleTag, userProfile.PatreonUserId);
            
            _logger.LogInformation("Successfully linked BattleTag {BattleTag} to Patreon user {PatreonUserId}", 
                battleTag, userProfile.PatreonUserId);

            return new PatreonOAuthResult
            {
                Success = true,
                PatreonUserId = userProfile.PatreonUserId,
                LinkedAt = accountLink.LinkedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing Patreon OAuth flow for BattleTag {BattleTag}", battleTag);
            return new PatreonOAuthResult
            {
                Success = false,
                ErrorMessage = "An error occurred during the OAuth process"
            };
        }
    }

    /// <summary>
    /// Exchange authorization code for access token
    /// </summary>
    private async Task<PatreonTokenResponse> ExchangeCodeForToken(string code, string redirectUri)
    {
        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
        {
            _logger.LogError("Patreon OAuth credentials not configured");
            return null;
        }

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret
        };

        using var content = new FormUrlEncodedContent(parameters);
        
        try
        {
            var response = await _httpClient.PostAsync(PatreonTokenUrl, content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Patreon token exchange failed. Status: {StatusCode}, Error: {Error}, RedirectUri: {RedirectUri}", 
                    response.StatusCode, errorContent, redirectUri);
                return null;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<PatreonTokenResponse>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return tokenResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during Patreon token exchange");
            return null;
        }
    }

    /// <summary>
    /// Get Patreon user profile using access token
    /// </summary>
    private async Task<PatreonUserProfile> GetPatreonUserProfile(string accessToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, PatreonUserUrl);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Patreon user profile request failed. Status: {StatusCode}, Error: {Error}", 
                    response.StatusCode, errorContent);
                return null;
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonSerializer.Deserialize<PatreonIdentityResponse>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (apiResponse?.Data?.Id == null)
            {
                _logger.LogError("Invalid Patreon identity response - missing user ID");
                return null;
            }

            return new PatreonUserProfile
            {
                PatreonUserId = apiResponse.Data.Id
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during Patreon user profile retrieval");
            return null;
        }
    }

    /// <summary>
    /// Validate state parameter for CSRF protection
    /// </summary>
    public bool ValidateState(string receivedState, string expectedState)
    {
        if (string.IsNullOrEmpty(receivedState) || string.IsNullOrEmpty(expectedState))
        {
            return false;
        }
        
        return receivedState.Equals(expectedState, StringComparison.Ordinal);
    }

    /// <summary>
    /// Check if BattleTag is linked to Patreon account
    /// </summary>
    public async Task<PatreonLinkStatus> GetLinkStatus(string battleTag)
    {
        try
        {
            var link = await _patreonLinkRepository.GetByBattleTag(battleTag);
            
            return new PatreonLinkStatus
            {
                IsLinked = link != null,
                PatreonUserId = link?.PatreonUserId,
                LinkedAt = link?.LinkedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Patreon link status for BattleTag {BattleTag}", battleTag);
            return new PatreonLinkStatus { IsLinked = false };
        }
    }

    /// <summary>
    /// Remove Patreon account link
    /// </summary>
    public async Task<bool> UnlinkAccount(string battleTag)
    {
        try
        {
            var result = await _patreonLinkRepository.RemoveByBattleTag(battleTag);
            
            if (result)
            {
                _logger.LogInformation("Successfully unlinked Patreon account for BattleTag {BattleTag}", battleTag);
            }
            else
            {
                _logger.LogWarning("No Patreon link found to remove for BattleTag {BattleTag}", battleTag);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unlinking Patreon account for BattleTag {BattleTag}", battleTag);
            return false;
        }
    }
}

public class PatreonOAuthResult
{
    public bool Success { get; set; }
    public string PatreonUserId { get; set; }
    public DateTime? LinkedAt { get; set; }
    public string ErrorMessage { get; set; }
}

public class PatreonLinkStatus
{
    public bool IsLinked { get; set; }
    public string PatreonUserId { get; set; }
    public DateTime? LinkedAt { get; set; }
}

public class PatreonTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; }

    [JsonPropertyName("scope")]
    public string Scope { get; set; }

    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }
}

public class PatreonUserProfile
{
    public string PatreonUserId { get; set; }
}

public class PatreonIdentityResponse
{
    public PatreonIdentityData Data { get; set; }
}

public class PatreonIdentityData
{
    public string Id { get; set; }
    public string Type { get; set; }
}