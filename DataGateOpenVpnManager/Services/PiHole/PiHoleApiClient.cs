using System.Text;
using DataGateOpenVpnManager.Models;
using Newtonsoft.Json.Linq;

namespace DataGateOpenVpnManager.Services.PiHole;

public sealed class PiHoleApiClient(
    HttpClient httpClient,
    IPiHoleRuntimeOptionsStore runtimeOptions,
    ILogger<PiHoleApiClient> logger) : IPiHoleApiClient
{
    private string? _sessionId;
    private DateTimeOffset _sessionExpiresUtc = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    public async Task<PiHoleQueryFetchResult> GetQueriesSinceAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset untilUtc,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var options = runtimeOptions.GetEffective();
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return new PiHoleQueryFetchResult();

        await EnsureSessionAsync(options, cancellationToken);

        var fromUnix = fromUtc.ToUnixTimeSeconds();
        var untilUnix = untilUtc.ToUnixTimeSeconds();
        var pageSize = Math.Max(1, Math.Min(maxCount, 1000));
        var collected = new List<PiHoleQueryRecord>();
        var offset = 0;

        while (collected.Count < maxCount)
        {
            var length = Math.Min(pageSize, maxCount - collected.Count);
            var url =
                $"api/queries?from={fromUnix}&until={untilUnix}&length={length}&start={offset}";

            var body = await SendGetAsync(options, url, cancellationToken);
            if (body is null)
                break;

            var page = PiHoleQueryParser.ParseQueriesResponse(body);
            if (page.Count == 0)
                break;

            collected.AddRange(page);
            var total = PiHoleQueryParser.ReadRecordsTotal(body);
            offset += page.Count;

            if (page.Count < length)
                break;
            if (total.HasValue && offset >= total.Value)
                break;
            if (collected.Count >= maxCount)
                break;
        }

        var totalFromApi = collected.Count;
        var filtered = PiHoleSubnetFilter.Apply(collected, options.ClientSubnetPrefix);
        if (totalFromApi != filtered.Count)
        {
            logger.LogDebug(
                "Pi-hole queries subnet filter: fetched={Fetched}, afterFilter={AfterFilter}, prefix={Prefix}",
                totalFromApi, filtered.Count, options.ClientSubnetPrefix);
        }

        return new PiHoleQueryFetchResult
        {
            Records = filtered,
            TotalFromApi = totalFromApi
        };
    }

    public async Task<(bool Authenticated, int SampleQueryCount, string? Error)> ProbeAsync(
        CancellationToken cancellationToken)
    {
        var options = runtimeOptions.GetEffective();
        if (!options.Enabled)
            return (false, 0, "Pi-hole collector is disabled.");

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return (false, 0, "Pi-hole BaseUrl is empty.");

        try
        {
            await EnsureSessionAsync(options, cancellationToken);
            var until = DateTimeOffset.UtcNow;
            var from = until.AddMinutes(-1);
            var url =
                $"api/queries?from={from.ToUnixTimeSeconds()}&until={until.ToUnixTimeSeconds()}&length=5";
            var body = await SendGetAsync(options, url, cancellationToken);
            if (body is null)
                return (false, 0, "Pi-hole queries request failed (see microservice logs for HTTP details).");

            var records = PiHoleQueryParser.ParseQueriesResponse(body);
            var filtered = PiHoleSubnetFilter.Apply(records, options.ClientSubnetPrefix);
            var authenticated = string.IsNullOrWhiteSpace(options.AppPassword) || _sessionId is not null;
            if (!authenticated)
                return (false, filtered.Count, "Pi-hole authentication failed (invalid app password or auth endpoint error).");

            return (authenticated, filtered.Count, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Pi-hole probe failed for BaseUrl={BaseUrl}", options.BaseUrl);
            return (false, 0, ex.Message);
        }
    }

    private async Task<string?> SendGetAsync(PiHoleOptions options, string url, CancellationToken cancellationToken)
    {
        var target = BuildUri(options, url);
        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        if (!string.IsNullOrWhiteSpace(_sessionId))
            request.Headers.TryAddWithoutValidation("sid", _sessionId);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Pi-hole GET {Url} failed: status={StatusCode}, body={Body}",
                url,
                (int)response.StatusCode,
                Truncate(body, 300));
            return null;
        }

        return body;
    }

    private async Task EnsureSessionAsync(PiHoleOptions options, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_sessionId) && DateTimeOffset.UtcNow < _sessionExpiresUtc)
            return;

        if (string.IsNullOrWhiteSpace(options.AppPassword))
            return;

        await _authLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_sessionId) && DateTimeOffset.UtcNow < _sessionExpiresUtc)
                return;

            var authUrl = BuildUri(options, "api/auth");
            using var request = new HttpRequestMessage(HttpMethod.Post, authUrl)
            {
                Content = new StringContent(
                    $"{{\"password\":{JValue.CreateString(options.AppPassword).ToString()}}}",
                    Encoding.UTF8,
                    "application/json")
            };

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Pi-hole auth POST api/auth failed: BaseUrl={BaseUrl}, status={StatusCode}, body={Body}",
                    options.BaseUrl,
                    (int)response.StatusCode,
                    Truncate(body, 200));
                _sessionId = null;
                return;
            }

            _sessionId = PiHoleQueryParser.ReadSessionId(body);
            if (string.IsNullOrWhiteSpace(_sessionId))
            {
                logger.LogWarning(
                    "Pi-hole auth succeeded but session id missing in response. BaseUrl={BaseUrl}",
                    options.BaseUrl);
                return;
            }

            var validitySeconds = JObject.Parse(body)["session"]?["validity"]?.Value<int>() ?? 1800;
            _sessionExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, validitySeconds - 30));
            logger.LogInformation(
                "Pi-hole API session established. BaseUrl={BaseUrl}, validitySec={ValiditySeconds}",
                options.BaseUrl,
                validitySeconds);
        }
        finally
        {
            _authLock.Release();
        }
    }

    private static Uri BuildUri(PiHoleOptions options, string relative) =>
        new(new Uri(options.BaseUrl.TrimEnd('/') + "/"), relative);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";
}
