using System.Net;
using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.PiHole;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DataGateOpenVpnManager.Tests.Services.PiHole;

public class PiHoleApiClientTests
{
    [Fact]
    public async Task GetQueriesSinceAsync_AuthenticatesAndParsesQueries()
    {
        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            calls++;
            if (calls == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"session":{"sid":"sid-1","validity":1800}}""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "queries": [
                        {
                          "id": 99,
                          "time": 1719043200,
                          "domain": "youtube.com",
                          "client": {"ip": "10.51.30.2"},
                          "status": "FORWARDED"
                        }
                      ]
                    }
                    """)
            };
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8080/") };
        var store = new PiHoleRuntimeOptionsStore(new TestOptionsMonitor(new PiHoleOptions()));
        store.Apply(new PiHoleOptions
        {
            Enabled = true,
            BaseUrl = "http://127.0.0.1:8080",
            AppPassword = "secret"
        });

        var sut = new PiHoleApiClient(client, store, NullLogger<PiHoleApiClient>.Instance);

        var fetch = await sut.GetQueriesSinceAsync(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            100,
            CancellationToken.None);

        Assert.Single(fetch.Records);
        Assert.Equal("youtube.com", fetch.Records[0].Domain);
        Assert.Equal(2, calls);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class TestOptionsMonitor(PiHoleOptions current) : IOptionsMonitor<PiHoleOptions>
    {
        public PiHoleOptions CurrentValue { get; } = current;
        public PiHoleOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PiHoleOptions, string?> listener) => null;
    }
}
