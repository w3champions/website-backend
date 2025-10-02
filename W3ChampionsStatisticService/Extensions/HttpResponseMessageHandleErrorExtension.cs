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
            try
            {
                // If an explicit error result is set, add it to the exception message.
                var errorResult = JsonConvert.DeserializeObject<ErrorResult>(content);
                if (errorResult?.Error != null)
                {
                    throw new HttpRequestException(errorResult.Error, null, response.StatusCode);
                }
            }
            catch (Exception)
            {
                // Ignore JSON parsing errors
            }
            // Otherwise, do not include unparsed body as it could be sensitive
            throw new HttpRequestException(null, null, response.StatusCode);
        }
    }
}
