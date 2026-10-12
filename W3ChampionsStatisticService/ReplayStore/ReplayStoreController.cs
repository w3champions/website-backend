using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Stripe;
using W3C.Contracts.Admin.Permission;
using W3C.Contracts.Matchmaking;
using W3C.Domain.Tracing;
using W3C.Domain.UpdateService;
using W3ChampionsStatisticService.Admin.ReplayExport;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.ReplayStore;

/// <summary>
/// The public replay store: any signed-in user finds games, pays with Stripe Checkout and downloads the
/// replays as one ZIP. The search and the download are the same as the admin export
/// (<see cref="ReplayExportController"/>); here the user pays first.
///
/// A purchase: <c>POST checkout</c> saves the order and gives the Stripe payment page. Stripe sends the
/// buyer back to <c>/replays?order=…</c>, and the page asks <c>POST orders/{id}/confirm</c>. The webhook
/// confirms the payment also when the buyer does not come back. The purchase is a ReplayExports record, so
/// the buyer downloads it, and downloads it again, from the history.
///
/// Until the public launch, only admins with the Replays permission use the store
/// (<see cref="StorePreviewPermission"/>). For the launch, remove <c>OnlyWithPermission</c> from the actions.
/// </summary>
[ApiController]
[Route("api/replay-store")]
[Trace]
public class ReplayStoreController(
    ReplayExportRepository repository,
    ReplayExportTicketStore tickets,
    ReplayServiceClient replayServiceClient,
    ReplayOrderRepository orders,
    ReplayOrderFulfillment fulfillment,
    IReplayPayments payments,
    IMemoryCache cache,
    ILogger<ReplayStoreController> logger) : ReplayExportControllerBase(repository, tickets, replayServiceClient, logger)
{
    /// <summary>The store is not open to all users yet: only admins with this permission.</summary>
    private const EPermission StorePreviewPermission = EPermission.Replays;

    /// <summary>Separate from the admin exports: buyers cannot block the admins.</summary>
    private static readonly SemaphoreSlim StoreExports = new(MaxRunningExports, MaxRunningExports);

    /// <summary>Searches that can run at the same time. Each one can take Mongo for up to 20 s.</summary>
    private const int MaxRunningSearches = 4;

    private static readonly SemaphoreSlim RunningSearches = new(MaxRunningSearches, MaxRunningSearches);
    private static readonly TimeSpan SearchWait = TimeSpan.FromSeconds(10);

    /// <summary>Payment pages that one user can open in one hour. Each order keeps up to 5,000 game ids.</summary>
    private const int MaxCheckoutsPerHour = 10;

    /// <summary>A Checkout Session event is a few KB. The limit of the server is 128 MB.</summary>
    private const long MaxWebhookBytes = 256 * 1024;

    /// <summary>As the cache of <c>api/matches/map-names</c>.</summary>
    private static readonly TimeSpan OptionsCacheTime = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan ConfirmInterval = TimeSpan.FromSeconds(2);
    private static readonly object ConfirmLock = new();

    private readonly ReplayOrderRepository _orders = orders;
    private readonly ReplayOrderFulfillment _fulfillment = fulfillment;
    private readonly IReplayPayments _payments = payments;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<ReplayStoreController> _logger = logger;

    protected override SemaphoreSlim RunningExports => StoreExports;

    protected override string TicketScope => "store";

    /// <summary>
    /// Without sign-in: the page shows the form and the prices before the user signs in. Cached, because the
    /// map names read the matches of the season and anyone can call this.
    /// </summary>
    [HttpGet("options")]
    public async Task<IActionResult> GetOptions([FromQuery] GameMode gameMode = GameMode.GM_1v1)
    {
        if (!Enum.IsDefined(gameMode)) return BadRequest(new { Error = "Unknown game mode." });

        var options = await _cache.GetOrCreateAsync($"replay-store-options:{gameMode}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = OptionsCacheTime;
            return Repository.LoadOptions(gameMode, new ReplayExportPrices { Free = false, Available = _payments.IsConfigured });
        });
        return Ok(options);
    }

    [HttpPost("preview")]
    [BearerSignedIn(OnlyWithPermission = StorePreviewPermission)]
    public async Task<IActionResult> Preview([FromBody] ReplayExportFilter filter, string battleTag)
    {
        if (!await RunningSearches.WaitAsync(SearchWait))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { Error = "Many searches are running. Try again in a moment." });
        }

        try
        {
            return await Search(filter, battleTag, onlyPurchases: true);
        }
        finally
        {
            RunningSearches.Release();
        }
    }

    [HttpPost("matches")]
    [BearerSignedIn(OnlyWithPermission = StorePreviewPermission)]
    public Task<IActionResult> LoadMatches([FromBody] ReplayExportMatchesRequest request) => TablePage(request);

    /// <summary>Saves the order and opens a Stripe payment page for it. The server sets the price.</summary>
    [HttpPost("checkout")]
    [BearerSignedIn(OnlyWithPermission = StorePreviewPermission)]
    public async Task<IActionResult> Checkout([FromBody] ReplayExportTicketRequest request, string battleTag)
    {
        if (!_payments.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { Error = "Payments are not available right now. Try again later." });
        }

        var website = ReplayStoreUrls.Website(Request.Headers.Origin);
        if (website == null) return BadRequest(new { Error = "Payments start only on the W3Champions website." });

        var max = ReplayExportRepository.MaxQuantity;
        var error = ParseMatchIds(request?.MatchIds, max, $"At most {max:N0} replays per purchase.", out var ids);
        if (error != null) return BadRequest(new { Error = error });
        if (ids.Count == 0) return BadRequest(new { Error = "Select at least one game." });

        // Only games with a replay: the buyer pays for what the ZIP can have.
        var replayable = await Repository.ReplayableIds(ids);
        if (replayable.Count == 0) return BadRequest(new { Error = "None of the selected games has a replay." });

        var order = new ReplayOrder
        {
            Id = ObjectId.GenerateNewId(),
            BattleTag = battleTag,
            CreatedAt = DateTime.UtcNow,
            MatchIds = replayable,
            // Only for the manifest and the history. A filter that a search would not accept is not kept.
            Filter = request.Filter != null && Validate(request.Filter) == null ? request.Filter : null,
            Label = CleanText(request.Label, 100),
            Description = CleanText(request.Description, 500),
            Amount = ReplayExportPricing.Price(replayable.Count),
            Currency = ReplayExportPricing.Currency,
            Status = ReplayOrderStatus.Pending,
        };
        // The order first, then the count with it: of many requests at the same time, only the first ones pass.
        await _orders.Create(order);
        if (await _orders.CountStartedSince(battleTag, DateTime.UtcNow.AddHours(-1)) > MaxCheckoutsPerHour)
        {
            await _orders.Delete(order.Id);
            return StatusCode(StatusCodes.Status429TooManyRequests, new { Error = "You opened many payment pages in the last hour. Try again later." });
        }

        ReplayCheckoutSession session;
        try
        {
            var back = $"{website}/replays?order={order.Id}";
            // Stripe puts the session id in place of {CHECKOUT_SESSION_ID}.
            session = await _payments.CreateCheckout(order, $"{back}&session_id={{CHECKOUT_SESSION_ID}}", $"{back}&canceled=1");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe did not open a payment page for replay order {OrderId}", order.Id);
            await _orders.Close(order.Id, ReplayOrderStatus.Failed);
            return StatusCode(StatusCodes.Status502BadGateway, new { Error = "The payment page did not open. Try again." });
        }

        await _orders.SetCheckoutSession(order.Id, session.Id);
        _logger.LogInformation("Replay order {OrderId} by {BattleTag}: {Count} replays, {Amount} {Currency}", order.Id, battleTag, replayable.Count, order.Amount, order.Currency);
        return Ok(new ReplayCheckoutResponse
        {
            OrderId = order.Id.ToString(),
            Url = session.Url,
            Count = replayable.Count,
            Amount = order.Amount,
            Currency = order.Currency,
        });
    }

    /// <summary>
    /// The return page asks for its order. A pending order gets the state of its payment from Stripe, so the
    /// buyer can download at once, also before the webhook arrives.
    /// </summary>
    [HttpPost("orders/{orderId}/confirm")]
    [BearerSignedIn(OnlyWithPermission = StorePreviewPermission)]
    public async Task<IActionResult> ConfirmOrder([FromRoute] string orderId, string battleTag)
    {
        var order = await _orders.LoadForUser(orderId, battleTag);
        if (order == null) return NotFound(new { Error = "This order does not exist." });

        // At most one Stripe request per order in a few seconds, also when the page (or a script) asks more often.
        if (order.Status == ReplayOrderStatus.Pending && order.CheckoutSessionId != null && _payments.IsConfigured
            && StripeCheckDue(order.Id))
        {
            try
            {
                var session = await _payments.GetCheckout(order.CheckoutSessionId);
                if (session != null) order = await _fulfillment.Apply(order, session);
            }
            // Stripe.net gives network errors and timeouts as they are, not as StripeException.
            catch (Exception ex) when (ex is StripeException or HttpRequestException
                || (ex is OperationCanceledException && !HttpContext.RequestAborted.IsCancellationRequested))
            {
                // Stripe is busy or cannot be reached: the order stays pending, and the page asks again. The
                // webhook also delivers the purchase.
                _logger.LogWarning("Replay order {OrderId}: Stripe did not give the session: {Message}", order.Id, ex.Message);
            }
        }
        return Ok(ReplayOrderView.From(order));
    }

    private bool StripeCheckDue(ObjectId orderId)
    {
        var key = $"replay-order-check:{orderId}";
        lock (ConfirmLock)
        {
            if (_cache.TryGetValue(key, out _)) return false;
            _cache.Set(key, true, ConfirmInterval);
            return true;
        }
    }

    [HttpGet("purchases")]
    [BearerSignedIn(OnlyWithPermission = StorePreviewPermission)]
    public async Task<IActionResult> GetPurchases(string battleTag)
    {
        return Ok(await Repository.LoadPurchases(battleTag));
    }

    /// <summary>The account menu shows the link to the purchases only when there are some.</summary>
    [HttpGet("purchases/count")]
    [BearerSignedIn(OnlyWithPermission = StorePreviewPermission)]
    public async Task<IActionResult> CountPurchases(string battleTag)
    {
        return Ok(new { Count = await Repository.CountPurchases(battleTag) });
    }

    /// <summary>A download of a purchase. The first one and every later one cost nothing more.</summary>
    [HttpPost("purchases/{exportId}/tickets")]
    [BearerSignedIn(OnlyWithPermission = StorePreviewPermission)]
    public async Task<IActionResult> CreatePurchaseTicket([FromRoute] string exportId, string battleTag)
    {
        var record = await Repository.LoadExport(exportId, battleTag);
        return RepeatTicket(record?.Charged == true ? record : null, battleTag);
    }

    [HttpGet("download/{ticket}")]
    public Task<IActionResult> Download([FromRoute] string ticket) => StreamDownload(ticket);

    /// <summary>
    /// Stripe events. Only the signature makes an event valid. An error answer makes Stripe send the event
    /// again later.
    /// </summary>
    [HttpPost("stripe/webhook")]
    [RequestSizeLimit(MaxWebhookBytes)]
    public async Task<IActionResult> StripeWebhook()
    {
        if (!_payments.IsConfigured)
        {
            // Stripe sends the event again later, when the secret key is set.
            _logger.LogError("A Stripe webhook arrived, but STRIPE_SECRET_KEY is not set");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        string payload;
        try
        {
            using var reader = new StreamReader(Request.Body);
            payload = await reader.ReadToEndAsync();
        }
        catch (BadHttpRequestException ex)
        {
            // Larger than MaxWebhookBytes (413), or the client stopped sending.
            return StatusCode(ex.StatusCode);
        }

        var paymentEvent = _payments.ParseWebhook(payload, Request.Headers["Stripe-Signature"]);
        if (paymentEvent == null) return BadRequest();
        if (paymentEvent.CheckoutSessionId == null) return Ok();

        switch (paymentEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
            case EventTypes.CheckoutSessionAsyncPaymentSucceeded:
            case EventTypes.CheckoutSessionExpired:
                await _fulfillment.Sync(paymentEvent.CheckoutSessionId);
                break;
            case EventTypes.CheckoutSessionAsyncPaymentFailed:
                await _fulfillment.Sync(paymentEvent.CheckoutSessionId, paymentFailed: true);
                break;
        }
        return Ok();
    }
}

public static class ReplayStoreUrls
{
    /// <summary>
    /// The website that Stripe sends the buyer back to: the origin of the checkout request, when it is a
    /// W3Champions site (https) or a local development server. Null for any other origin.
    /// </summary>
    public static string Website(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return null;
        var w3c = uri.Scheme == Uri.UriSchemeHttps && (uri.Host == "w3champions.com" || uri.Host.EndsWith(".w3champions.com", StringComparison.Ordinal));
        var local = uri.IsLoopback && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        return w3c || local ? uri.GetLeftPart(UriPartial.Authority) : null;
    }
}
