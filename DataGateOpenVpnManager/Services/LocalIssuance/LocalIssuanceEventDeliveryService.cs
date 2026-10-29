using DataGateMonitor.SharedModels.DataGateMonitor.LocalIssuance.Requests;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace DataGateOpenVpnManager.Services.LocalIssuance;

/// <summary>
/// Drains <see cref="ILocalIssuanceOutbox"/> to the backend in order, signing each request with the node key.
/// Network errors, 5xx and 401/403 (key not trusted yet) are retried with backoff; the event stays on disk.
/// </summary>
public sealed class LocalIssuanceEventDeliveryService(
    IHttpClientFactory httpClientFactory,
    ILocalIssuanceOutbox outbox,
    INodeIdentity identity,
    TimeProvider timeProvider,
    ILogger<LocalIssuanceEventDeliveryService> logger) : BackgroundService
{
    public const string HttpClientName = nameof(LocalIssuanceEventDeliveryService);
    public const string EventsPath = "api/open-vpn-servers/local-issuance-events";

    internal static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IdlePoll = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = MinBackoff;
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                if (await DeliverPendingAsync(stoppingToken))
                {
                    wait = IdlePoll;
                    backoff = MinBackoff;
                }
                else
                {
                    wait = backoff;
                    backoff = Next(backoff);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Local issuance event delivery pass failed.");
                wait = backoff;
                backoff = Next(backoff);
            }

            try
            {
                await outbox.WaitForEnqueueAsync(wait, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>One pass over the outbox. Returns true when it is empty afterwards.</summary>
    internal async Task<bool> DeliverPendingAsync(CancellationToken cancellationToken)
    {
        foreach (var item in outbox.ListPending())
        {
            var outcome = await SendAsync(item.Event, cancellationToken);
            switch (outcome)
            {
                case DeliveryOutcome.Delivered:
                    outbox.Complete(item);
                    logger.LogInformation("Delivered {EventType} for {CommonName} (serial {Serial}).",
                        item.Event.EventType, item.Event.CommonName, item.Event.SerialNumber);
                    break;
                case DeliveryOutcome.Rejected rejected:
                    outbox.Reject(item, rejected.Reason);
                    break;
                default:
                    // Keep order: a later revoke must not overtake its issue.
                    return false;
            }
        }

        return true;
    }

    private async Task<DeliveryOutcome> SendAsync(LocalIssuanceEventRequest evt, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(evt));

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(client.BaseAddress!, EventsPath));
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        NodeRequestSignature.Apply(request, identity, body, timeProvider.GetUtcNow(), EventsPath);

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return DeliveryOutcome.Delivered.Instance;

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
                return new DeliveryOutcome.Rejected($"{(int)response.StatusCode}: {Truncate(text)}");

            logger.LogWarning(
                "Backend answered {StatusCode} for {EventType} {CommonName} (serial {Serial}); will retry. {Body}",
                (int)response.StatusCode, evt.EventType, evt.CommonName, evt.SerialNumber, Truncate(text));
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                logger.LogWarning(
                    "Node key {Fingerprint} is not trusted by the backend yet — approve this node in the dashboard.",
                    identity.Fingerprint);
            return DeliveryOutcome.Retry.Instance;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Backend unreachable for {EventType} {CommonName}: {Message}; will retry.",
                evt.EventType, evt.CommonName, ex.Message);
            return DeliveryOutcome.Retry.Instance;
        }
    }

    private static TimeSpan Next(TimeSpan current) =>
        TimeSpan.FromTicks(Math.Min(current.Ticks * 2, MaxBackoff.Ticks));

    private static string Truncate(string value) => value.Length <= 256 ? value : value[..256] + "…";

    private abstract record DeliveryOutcome
    {
        public sealed record Delivered : DeliveryOutcome { public static readonly Delivered Instance = new(); }
        public sealed record Retry : DeliveryOutcome { public static readonly Retry Instance = new(); }
        public sealed record Rejected(string Reason) : DeliveryOutcome;
    }
}
