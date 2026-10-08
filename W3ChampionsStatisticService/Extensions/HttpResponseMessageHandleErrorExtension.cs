using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net;
using Newtonsoft.Json;
using W3ChampionsStatisticService.WebApi.ExceptionFilters;

namespace W3ChampionsStatisticService.Extensions;

public static class HttpResponseMessageHandleErrorExtension
{

    public static async Task ThrowIfError(
        this HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            string error = null;
            try
            {
                // If an explicit error result is set, add it to the exception message.
                error = JsonConvert.DeserializeObject<ErrorResult>(content)?.Error;
            }
            catch (JsonException)
            {
                // Ignore JSON parsing errors
            }

            // Only 4xx messages are meant for the caller; 5xx bodies may carry internal details.
            if (error != null && (int)response.StatusCode < 500)
            {
                throw new HttpRequestException(error, null, response.StatusCode);
            }

            // Otherwise, do not include unparsed body as it could be sensitive
            throw new HttpRequestException(null, null, response.StatusCode);
        }
    }
}
