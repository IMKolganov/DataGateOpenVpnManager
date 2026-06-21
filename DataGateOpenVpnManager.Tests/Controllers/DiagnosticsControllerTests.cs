using DataGateOpenVpnManager.Controllers;
using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.OpenVpnTelnet;
using DataGateOpenVpnManager.Services.Proxy;
using DataGateOpenVpnManager.Tests.Services.OpenVpnTelnet.Fakes;
using DataGateOpenVpnManager.Tests.Services.Proxy;
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
        OpenVpnProxyOptions? options = null)
    {
        var configuration = config ?? new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DNS1"] = "10.51.15.1" })
            .Build();

        return new DiagnosticsController(
            configuration,
            Options.Create(options ?? new OpenVpnProxyOptions()),
            active ?? new ActiveProxyConnectionService(),
            new ProxyTrafficFlowService(),
            cache ?? new OpenVpnManagementStatusCache(null!, new NoOpProxySessionAuditService(), NullLogger<OpenVpnManagementStatusCache>.Instance),
            audit ?? new ProxySessionAuditService(Options.Create(new OpenVpnProxyOptions { SessionAudit = true }), NullLogger<ProxySessionAuditService>.Instance),
            NullLogger<DiagnosticsController>.Instance);
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
        telnet.SimulateIncomingData(
            "CLIENT_LIST\tadg-77\t127.0.0.1:60123\t10.51.16.3\t\t1000\t2000\t1748337500\tUNDEF\nEND");
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
}
