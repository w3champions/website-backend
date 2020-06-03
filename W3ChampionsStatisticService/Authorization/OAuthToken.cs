using System;

namespace W3ChampionsStatisticService.Authorization
{
    public class OAuthToken
    {
        public string access_token { get; set; }
        public string token_type { get; set; }
        public int expires_in { get; set; }
        public DateTime CreateDate { get; set; }

        public bool hasExpired()
        {
            return DateTime.Now > CreateDate.Add(TimeSpan.FromSeconds(expires_in));
        }
    }
}