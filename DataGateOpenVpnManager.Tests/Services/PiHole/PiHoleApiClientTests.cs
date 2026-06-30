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

    [Fact]
    public async Task ProbeAsync_PostsValidJsonAuthBody()
    {
        string? authBody = null;
        var calls = 0;
        var handler = new StubHandler(req =>
        {
            calls++;
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
            {
                authBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"session":{"sid":"sid-1","validity":1800}}""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"queries":[]}""")
            };
        });

        var store = new PiHoleRuntimeOptionsStore(new TestOptionsMonitor(new PiHoleOptions()));
        store.Apply(new PiHoleOptions
        {
            Enabled = true,
            BaseUrl = "http://127.0.0.1:8080",
            AppPassword = "datagate2019"
        });

        var sut = new PiHoleApiClient(new HttpClient(handler), store, NullLogger<PiHoleApiClient>.Instance);
        await sut.ProbeAsync(CancellationToken.None);

        Assert.Equal("""{"password":"datagate2019"}""", authBody);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ProbeAsync_ReusesExistingSession_OnSecondCall()
    {
        var authCalls = 0;
        var handler = new StubHandler(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
            {
                authCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"session":{"sid":"sid-1","validity":1800}}""")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"queries":[]}""")
            };
        });

        var store = new PiHoleRuntimeOptionsStore(new TestOptionsMonitor(new PiHoleOptions()));
        store.Apply(new PiHoleOptions
        {
            Enabled = true,
            BaseUrl = "http://127.0.0.1:8080",
            AppPassword = "secret"
        });

        var sut = new PiHoleApiClient(new HttpClient(handler), store, NullLogger<PiHoleApiClient>.Instance);
        await sut.ProbeAsync(CancellationToken.None);
        await sut.ProbeAsync(CancellationToken.None);

        Assert.Equal(1, authCalls);
    }

    [Fact]
    public async Task SendGetAsync_LogsOutPreviousSession_WhenUnauthorized()
    {
        var logoutCalls = 0;
        var authCalls = 0;
        var queryCalls = 0;
        var handler = new StubHandler(req =>
        {
            if (req.Method == HttpMethod.Delete && req.RequestUri!.AbsolutePath.EndsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
            {
                logoutCalls++;
                Assert.Contains("sid=session-1", req.RequestUri.Query, StringComparison.Ordinal);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/api/auth", StringComparison.OrdinalIgnoreCase))
            {
                authCalls++;
                var sid = authCalls == 1 ? "session-1" : "session-2";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"session\":{\"sid\":\"" + sid + "\",\"validity\":1800}}")
                };
            }

            queryCalls++;
            if (queryCalls >= 2)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"queries":[]}""")
            };
        });

        var store = new PiHoleRuntimeOptionsStore(new TestOptionsMonitor(new PiHoleOptions()));
        store.Apply(new PiHoleOptions
        {
            Enabled = true,
            BaseUrl = "http://127.0.0.1:8080",
            AppPassword = "secret"
        });

        var sut = new PiHoleApiClient(new HttpClient(handler), store, NullLogger<PiHoleApiClient>.Instance);
        await sut.ProbeAsync(CancellationToken.None);
        await sut.ProbeAsync(CancellationToken.None);

        Assert.Equal(2, authCalls);
        Assert.Equal(1, logoutCalls);
        Assert.Equal(3, queryCalls);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsDisabledMessage_WhenCollectorDisabled()
    {
        var store = new PiHoleRuntimeOptionsStore(new TestOptionsMonitor(new PiHoleOptions { Enabled = false }));
        var sut = new PiHoleApiClient(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))), store, NullLogger<PiHoleApiClient>.Instance);

        var (authenticated, count, error) = await sut.ProbeAsync(CancellationToken.None);

        Assert.False(authenticated);
        Assert.Equal(0, count);
        Assert.Contains("disabled", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsError_WhenQueriesRequestFails()
    {
        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            calls++;
            return calls == 1
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"session":{"sid":"sid-1","validity":1800}}""")
                }
                : new HttpResponseMessage(HttpStatusCode.BadGateway)
                {
                    Content = new StringContent("gateway error")
                };
        });

        var store = new PiHoleRuntimeOptionsStore(new TestOptionsMonitor(new PiHoleOptions()));
        store.Apply(new PiHoleOptions
        {
            Enabled = true,
            BaseUrl = "http://127.0.0.1:8080",
            AppPassword = "secret"
        });

        var sut = new PiHoleApiClient(new HttpClient(handler), store, NullLogger<PiHoleApiClient>.Instance);
        var (_, count, error) = await sut.ProbeAsync(CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Contains("failed", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetQueriesSinceAsync_AppliesSubnetFilterAndReportsTotalFromApi()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "queries": [
                    {
                      "id": 1,
                      "time": 1719043200,
                      "domain": "vpn.example",
                      "client": {"ip": "10.51.30.2"},
                      "status": "FORWARDED"
                    },
                    {
                      "id": 2,
                      "time": 1719043201,
                      "domain": "other.example",
                      "client": {"ip": "192.168.1.5"},
                      "status": "FORWARDED"
                    }
                  ]
                }
                """)
        });

        var store = new PiHoleRuntimeOptionsStore(new TestOptionsMonitor(new PiHoleOptions()));
        store.Apply(new PiHoleOptions
        {
            Enabled = true,
            BaseUrl = "http://127.0.0.1:8080",
            ClientSubnetPrefix = "10.51.30."
        });

        var sut = new PiHoleApiClient(new HttpClient(handler), store, NullLogger<PiHoleApiClient>.Instance);
        var fetch = await sut.GetQueriesSinceAsync(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            100,
            CancellationToken.None);

        Assert.Equal(2, fetch.TotalFromApi);
        Assert.Single(fetch.Records);
        Assert.Equal("vpn.example", fetch.Records[0].Domain);
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
