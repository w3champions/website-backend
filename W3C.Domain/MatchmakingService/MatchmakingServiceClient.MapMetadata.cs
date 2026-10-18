using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using W3C.Contracts.Matchmaking;

namespace W3C.Domain.MatchmakingService;

public partial class MatchmakingServiceClient
{
    /// <summary>
    /// Every matchmaking map (GET /maps is not paginated). Unlike <see cref="GetMaps"/>, a failed or empty
    /// answer throws instead of being read as "no maps", for callers that must not act on a partial list.
    /// </summary>
    public async Task<GetMapsResponse> GetAllMapsOrThrow()
    {
        var response = await _httpClient.GetAsync($"{MatchmakingApiUrl}/maps");
        if (!response.IsSuccessStatusCode)
        {
            await HandleMMError(response);
        }

        var result = await GetResult<GetMapsResponse>(response);
        if (result?.Items == null)
        {
            throw new HttpRequestException("Matchmaking returned no map list", null, HttpStatusCode.BadGateway);
        }

        return result;
    }

    public async Task<GameMapMetadataUpdateResponse> UpdateGameMapMetadata(GameMapMetadataUpdateRequest updateRequest)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{MatchmakingApiUrl}/maps/game-map-metadata");
        AdminSecretProvider.AddTo(request.Headers);
        request.Content = new StringContent(SerializeData(updateRequest), Encoding.UTF8, "application/json");
        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            await HandleMMError(response);
        }

        var result = await GetResult<GameMapMetadataUpdateResponse>(response);
        if (result?.Results == null)
        {
            throw new HttpRequestException("Matchmaking returned no game map metadata results", null, HttpStatusCode.BadGateway);
        }

        return result;
    }
}
