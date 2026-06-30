using DataGateOpenVpnManager.Controllers;
using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.OpenVpnTelnet;
using DataGateOpenVpnManager.Services.PiHole;
using DataGateOpenVpnManager.Services.Proxy;
using DataGateOpenVpnManager.Tests.Services.OpenVpnTelnet.Fakes;
using DataGateOpenVpnManager.Tests.Services.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Diagnostics.Responses;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Enums;
using DataGateMonitor.SharedModels.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace DataGateOpenVpnManager.Tests.Controllers;

public class DiagnosticsControllerTests
{
    private static DiagnosticsController CreateController(
        IActiveProxyConnectionService? active = null,
        IOpenVpnManagementStatusCache? cache = null,
        IProxySessionAuditService? audit = null,
        IConfiguration? config = null,
        OpenVpnProxyOptions? options = null,
        IPiHoleRuntimeOptionsStore? runtimeOptions = null,
        IPiHoleApiClient? piHoleApiClient = null,
        IPiHoleCollectorStatusStore? statusStore = null)
    {
        var configuration = config ?? new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DNS1"] = "10.51.15.1" })
            .Build();

        var runtime = runtimeOptions ?? new PiHoleRuntimeOptionsStore(new TestOptionsMonitor(new PiHoleOptions()));
        var piHole = piHoleApiClient ?? new Mock<IPiHoleApiClient>().Object;
        var status = statusStore ?? new PiHoleCollectorStatusStore();

        return new DiagnosticsController(
            configuration,
            Options.Create(options ?? new OpenVpnProxyOptions()),
            active ?? new ActiveProxyConnectionService(),
            new ProxyTrafficFlowService(),
            cache ?? new OpenVpnManagementStatusCache(null!, new NoOpProxySessionAuditService(), NullLogger<OpenVpnManagementStatusCache>.Instance),
            audit ?? new ProxySessionAuditService(Options.Create(new OpenVpnProxyOptions { SessionAudit = true }), NullLogger<ProxySessionAuditService>.Instance),
            runtime,
            piHole,
            status,
            new Mock<IPiHoleQueryCursorStore>().Object,
            NullLogger<DiagnosticsController>.Instance);
    }

    private sealed class TestOptionsMonitor(PiHoleOptions current) : IOptionsMonitor<PiHoleOptions>
    {
        public PiHoleOptions CurrentValue { get; } = current;
        public PiHoleOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PiHoleOptions, string?> listener) => null;
    }

    [Fact]
    public void GetProxyAudit_ReturnsRecentEntries()
    {
        var audit = new ProxySessionAuditService(
            Options.Create(new OpenVpnProxyOptions { SessionAudit = true }),
            NullLogger<ProxySessionAuditService>.Instance);
        audit.Record(new ProxySessionAuditEntry
        {
            AtUtc = DateTime.UtcNow,
            Event = "proxy.connected",
            ConnectionId = "c1",
            Decision = "ok",
            Reason = "Udp"
        });

        var controller = CreateController(audit: audit);
        var result = controller.GetProxyAudit(10);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<ProxyAuditDiagnosticsResponse>>(ok.Value);
        Assert.True(response.Success);
        Assert.Single(response.Data!.Entries);
    }

    [Fact]
    public async Task GetProxySessions_MarksZombieOnlyWhenEvaluationAvailable()
    {
        var active = new ActiveProxyConnectionService();
        active.Add(new ActiveProxyConnection
        {
            ConnectionId = "live",
            Protocol = ProxyConnectionProtocol.Udp,
            RealClientIp = "203.0.113.1",
            RealClientPort = 50000,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 60123,
            TargetIp = "127.0.0.1",
            TargetPort = 1194,
            ConnectedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });

        var telnet = new FakeTelnetClient();
        var management = new OpenVpnManagementSignalService(telnet, NullLogger<CommandQueue>.Instance);
        var cache = new OpenVpnManagementStatusCache(management, new NoOpProxySessionAuditService(), NullLogger<OpenVpnManagementStatusCache>.Instance);
        var refreshTask = cache.RefreshAsync(CancellationToken.None);
        telnet.SimulateIncomingData("END");
        await refreshTask;

        var controller = CreateController(active, cache);
        var result = await controller.GetProxySessions(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<ProxySessionDiagnosticsResponse>>(ok.Value);
        Assert.True(response.Success);
        Assert.False(response.Data!.PeerEvaluationAvailable);
        Assert.Equal("client_list_empty", response.Data.PeerEvaluationSkipReason);
        var session = Assert.Single(response.Data.Sessions);
        Assert.True(session.MissingFromManagement);
        Assert.False(session.IsZombie);
    }

    [Fact]
    public async Task GetProxySessions_ReportsLiveSession_WhenPeerInManagement()
    {
        await AssertLiveSessionAsync(
            "CLIENT_LIST\tadg-77\t127.0.0.1:60123\t10.51.16.3\t\t1000\t2000\t1748337500\tUNDEF\nEND");
    }

    [Fact]
    public async Task GetProxySessions_ReportsLiveSession_WhenPeerUsesOpenVpn27RealAddress()
    {
        await AssertLiveSessionAsync(
            "CLIENT_LIST\tadg-77\ttcp4-server:127.0.0.1:60123\t10.51.16.3\t\t1000\t2000\t1748337500\tUNDEF\nEND");
    }

    private static async Task AssertLiveSessionAsync(string managementPayload)
    {
        var active = new ActiveProxyConnectionService();
        active.Add(new ActiveProxyConnection
        {
            ConnectionId = "live",
            Protocol = ProxyConnectionProtocol.Udp,
            RealClientIp = "203.0.113.1",
            RealClientPort = 50000,
            LocalProxyIp = "127.0.0.1",
            LocalProxyPort = 60123,
            TargetIp = "127.0.0.1",
            TargetPort = 1194,
            ConnectedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });

        var telnet = new FakeTelnetClient();
        var management = new OpenVpnManagementSignalService(telnet, NullLogger<CommandQueue>.Instance);
        var cache = new OpenVpnManagementStatusCache(management, new NoOpProxySessionAuditService(), NullLogger<OpenVpnManagementStatusCache>.Instance);
        var refreshTask = cache.RefreshAsync(CancellationToken.None);
        telnet.SimulateIncomingData(managementPayload);
        await refreshTask;

        var controller = CreateController(active, cache);
        var result = await controller.GetProxySessions(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<ProxySessionDiagnosticsResponse>>(ok.Value);
        var session = Assert.Single(response.Data!.Sessions);
        Assert.False(session.MissingFromManagement);
        Assert.False(session.IsZombie);
        Assert.Equal("adg-77", session.OpenVpnCommonName);
        Assert.Equal("host-default-route", response.Data.DnsProbeScope);
    }

    [Fact]
    public async Task GetPiHoleDiagnostics_ReturnsStatusAndProbeData()
    {
        var runtime = new PiHoleRuntimeOptionsStore(new TestOptionsMonitor(new PiHoleOptions
        {
            Enabled = true,
            BaseUrl = "http://pi-hole:8080",
            AppPassword = "secret",
            PollIntervalSeconds = 60
        }));
        var status = new PiHoleCollectorStatusStore();
        status.SetCollectorRunning(true);
        status.RecordPollFailure(DateTimeOffset.UtcNow, "timeout");

        var api = new Mock<IPiHoleApiClient>();
        api.Setup(x => x.ProbeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, 0, "auth failed"));

        var controller = CreateController(runtimeOptions: runtime, piHoleApiClient: api.Object, statusStore: status);
        var result = await controller.GetPiHoleDiagnostics(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApiResponse<PiHoleDiagnosticsResponse>>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("http://pi-hole:8080", response.Data!.BaseUrl);
        Assert.Equal("timeout", response.Data.LastPollError);
        Assert.Equal("auth failed", response.Data.Error);
    }
}
