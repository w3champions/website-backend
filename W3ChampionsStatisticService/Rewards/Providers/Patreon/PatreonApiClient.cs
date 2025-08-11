using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace W3ChampionsStatisticService.Rewards.Providers.Patreon;

public class PatreonApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _accessToken;
    private readonly string _campaignId;
    private const string BaseUrl = "https://www.patreon.com/api/oauth2/v2";

    public PatreonApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _accessToken = Environment.GetEnvironmentVariable("REWARDS_PATREON_ACCESS_TOKEN");
        _campaignId = Environment.GetEnvironmentVariable("REWARDS_PATREON_CAMPAIGN_ID");

        if (!string.IsNullOrEmpty(_accessToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _accessToken);
        }
    }

    public async Task<List<PatreonMember>> GetAllCampaignMembers()
    {
        if (string.IsNullOrEmpty(_accessToken))
        {
            Log.Warning("Patreon access token not configured, cannot fetch members");
            return new List<PatreonMember>();
        }

        if (string.IsNullOrEmpty(_campaignId))
        {
            Log.Warning("Patreon campaign ID not configured, cannot fetch members");
            return new List<PatreonMember>();
        }

        var allMembers = new List<PatreonMember>();
        string nextPageUrl = $"{BaseUrl}/campaigns/{_campaignId}/members" +
            "?include=currently_entitled_tiers,user" +
            "&fields[member]=patron_status,email,full_name,is_follower,last_charge_date,last_charge_status,lifetime_support_cents,currently_entitled_amount_cents,pledge_relationship_start" +
            "&fields[tier]=title,amount_cents" +
            "&fields[user]=email,full_name" +
            "&page[count]=500";

        try
        {
            while (!string.IsNullOrEmpty(nextPageUrl))
            {
                Log.Debug("Fetching Patreon members from: {Url}", nextPageUrl);

                var response = await _httpClient.GetAsync(nextPageUrl);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("Failed to fetch Patreon members. Status: {Status}, Error: {Error}",
                        response.StatusCode, errorContent);
                    break;
                }

                var content = await response.Content.ReadAsStringAsync();
                var apiResponse = JsonSerializer.Deserialize<PatreonApiResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (apiResponse?.Data != null)
                {
                    foreach (var memberData in apiResponse.Data)
                    {
                        var member = ParseMemberData(memberData, apiResponse.Included);
                        if (member != null)
                        {
                            allMembers.Add(member);
                        }
                    }
                }

                nextPageUrl = apiResponse?.Links?.Next;

                if (string.IsNullOrEmpty(nextPageUrl))
                {
                    break;
                }
            }

            Log.Information("Fetched {Count} Patreon members from API", allMembers.Count);
            return allMembers;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error fetching Patreon members");
            return allMembers;
        }
    }

    private PatreonMember ParseMemberData(PatreonApiData memberData, List<PatreonApiData> included)
    {
        try
        {
            var member = new PatreonMember
            {
                Id = memberData.Id,
                PatronStatus = memberData.Attributes?.GetValueOrDefault("patron_status")?.ToString(),
                Email = memberData.Attributes?.GetValueOrDefault("email")?.ToString(),
                LastChargeStatus = memberData.Attributes?.GetValueOrDefault("last_charge_status")?.ToString(),
                EntitledTierIds = new List<string>()
            };

            // Get user email from included data if not in member attributes
            if (string.IsNullOrEmpty(member.Email) && memberData.Relationships?.ContainsKey("user") == true)
            {
                var userRelation = memberData.Relationships["user"];
                if (userRelation?.Data is JsonElement userData)
                {
                    var userId = userData.GetProperty("id").GetString();
                    var user = included?.FirstOrDefault(i => i.Type == "user" && i.Id == userId);
                    if (user?.Attributes?.ContainsKey("email") == true)
                    {
                        member.Email = user.Attributes["email"]?.ToString();
                    }
                }
            }

            // Get entitled tiers
            if (memberData.Relationships?.ContainsKey("currently_entitled_tiers") == true)
            {
                var tiersRelation = memberData.Relationships["currently_entitled_tiers"];
                if (tiersRelation?.Data is JsonElement tiersData && tiersData.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tierElement in tiersData.EnumerateArray())
                    {
                        if (tierElement.TryGetProperty("id", out var idProp))
                        {
                            member.EntitledTierIds.Add(idProp.GetString());
                        }
                    }
                }
            }

            return member;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse member data for member {Id}", memberData?.Id);
            return null;
        }
    }
}

public class PatreonMember
{
    public string Id { get; set; }
    public string Email { get; set; }
    public string PatronStatus { get; set; }
    public string LastChargeStatus { get; set; }
    public List<string> EntitledTierIds { get; set; } = new();

    public bool IsActivePatron =>
        PatronStatus == "active_patron" &&
        LastChargeStatus == "Paid";
}

public class PatreonApiResponse
{
    public List<PatreonApiData> Data { get; set; }
    public List<PatreonApiData> Included { get; set; }
    public PatreonApiLinks Links { get; set; }
}

public class PatreonApiData
{
    public string Id { get; set; }
    public string Type { get; set; }
    public Dictionary<string, object> Attributes { get; set; }
    public Dictionary<string, PatreonApiRelationship> Relationships { get; set; }
}

public class PatreonApiRelationship
{
    public object Data { get; set; }
}

public class PatreonApiLinks
{
    public string Next { get; set; }
}