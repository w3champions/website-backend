using System.Text.Json.Serialization;

namespace W3ChampionsStatisticService.Rewards.Providers.KoFi;

public class KoFiWebhookData
{
    [JsonPropertyName("verification_token")]
    public string VerificationToken { get; set; }

    [JsonPropertyName("message_id")]
    public string MessageId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("is_public")]
    public bool IsPublic { get; set; }

    [JsonPropertyName("from_name")]
    public string FromName { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }

    [JsonPropertyName("amount")]
    public string Amount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; }

    [JsonPropertyName("kofi_transaction_id")]
    public string KofiTransactionId { get; set; }

    [JsonPropertyName("shop_item_id")]
    public string ShopItemId { get; set; }

    [JsonPropertyName("tier_id")]
    public string TierId { get; set; }

    [JsonPropertyName("is_subscription_payment")]
    public bool IsSubscription { get; set; }
}
