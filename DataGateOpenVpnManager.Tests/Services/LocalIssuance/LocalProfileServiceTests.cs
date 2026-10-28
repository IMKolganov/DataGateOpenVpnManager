using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Cert.Responses;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.OvpnFile.Responses;
using DataGateMonitor.SharedModels.Enums;
using DataGateOpenVpnManager.Helpers;
using DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;
using DataGateOpenVpnManager.Services.Interfaces;
using DataGateOpenVpnManager.Services.LocalIssuance;
using DataGateOpenVpnManager.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services.LocalIssuance;

public class LocalProfileServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly Mock<IOvpnFileService> _ovpn = new();
    private readonly Mock<IEasyRsaService> _easyRsa = new();
    private readonly Mock<ILocalIssuanceOutbox> _outbox = new();
    private readonly List<LocalIssuanceEvent> _events = [];
    private readonly List<ServerCertificate> _index = [];
    private readonly LocalProfileRegistry _registry;
    private readonly LocalIssuanceOptions _options;
    private readonly ClientConfigDirStore _ccd;
    private OvpnIssueRequest? _lastIssue;

    public LocalProfileServiceTests()
    {
        _options = new LocalIssuanceOptions { Enabled = true, Directory = _dir.Combine("li") };
        _registry = new LocalProfileRegistry(_options);
        _ccd = new ClientConfigDirStore(new LocalIssuanceOptions { ClientConfigDir = _dir.Combine("ccd") },
            NullLogger<ClientConfigDirStore>.Instance);
        Directory.CreateDirectory(_dir.Combine("easy-rsa", "pki", "issued"));

        _easyRsa.Setup(e => e.GetAllCertificateInfoInIndexFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _index.ToList());
        _outbox.Setup(o => o.EnqueueAsync(It.IsAny<LocalIssuanceEvent>(), It.IsAny<CancellationToken>()))
            .Callback<LocalIssuanceEvent, CancellationToken>((e, _) => _events.Add(e))
            .Returns(Task.CompletedTask);
        _ovpn.Setup(o => o.IssueOvpnFile(It.IsAny<OvpnIssueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OvpnIssueRequest r, CancellationToken _) =>
            {
                _lastIssue = r;
                return new OvpnIssueResult(
                    new OvpnFileMetadata { CommonName = r.CommonName, FileName = $"{r.CommonName}.ovpn", FilePath = $"/x/{r.CommonName}.ovpn", IssuedTo = r.IssuedTo, CertFilePath = "c", KeyFilePath = "k" },
                    new ServerCertificate { CommonName = r.CommonName, SerialNumber = "5E71A1", Status = CertificateStatus.Active },
                    new string('f', 64));
            });
    }

    public void Dispose() => _dir.Dispose();

    private LocalProfileService Create(Dictionary<string, string?>? config = null)
    {
        var paths = new Mock<IEasyRsaPathResolver>();
        paths.Setup(p => p.GetEasyRsaPath()).Returns(_dir.Combine("easy-rsa"));
        return new LocalProfileService(_options, _ovpn.Object, _easyRsa.Object, paths.Object, _registry, _outbox.Object, _ccd,
            new ConfigurationBuilder().AddInMemoryCollection(config ?? new() { ["PUBLIC_IP"] = "203.0.113.10", ["PORT"] = "1295" }).Build(),
            TimeProvider.System, NullLogger<LocalProfileService>.Instance);
    }

    [Fact]
    public async Task Create_ServiceProfile_UsesSharedPipeline_RecordsAndQueuesEvent()
    {
        var view = await Create().CreateAsync(
            new LocalProfileCreateRequest { CommonName = "db1", IsServiceProfile = true, StaticIp = "10.50.28.201" },
            CancellationToken.None);

        Assert.NotNull(_lastIssue);
        Assert.True(_lastIssue!.IsServiceProfile);
        Assert.Equal(("10.50.28.201", "203.0.113.10", 1295, LocalProfileService.IssuedToLocalService),
            (_lastIssue.StaticIp, _lastIssue.ServerIp, _lastIssue.ServerPort, _lastIssue.IssuedTo));

        var evt = Assert.Single(_events);
        Assert.Equal((LocalIssuanceEventTypes.Issued, "local", "5E71A1", true, "10.50.28.201", new string('f', 64)),
            (evt.EventType, evt.Origin, evt.SerialNumber, evt.IsServiceProfile, evt.StaticIp, evt.CertFingerprintSha256));
        Assert.Equal(("local", "active"), (view.Origin, view.Status));
        Assert.NotNull(_registry.FindActive("db1"));
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("../etc")]
    [InlineData("server")]
    public async Task Create_InvalidCn_IsRejected(string cn)
    {
        await Assert.ThrowsAsync<LocalIssuanceValidationException>(() =>
            Create().CreateAsync(new LocalProfileCreateRequest { CommonName = cn }, CancellationToken.None));
        Assert.Empty(_events);
    }

    [Fact]
    public async Task Create_CnWithValidCertificate_IsConflict()
    {
        _index.Add(new ServerCertificate { CommonName = "db1", Status = CertificateStatus.Active, SerialNumber = "01" });

        var ex = await Assert.ThrowsAsync<LocalIssuanceValidationException>(() =>
            Create().CreateAsync(new LocalProfileCreateRequest { CommonName = "db1" }, CancellationToken.None));

        Assert.True(ex.IsConflict);
        _ovpn.Verify(o => o.IssueOvpnFile(It.IsAny<OvpnIssueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_CnOnlyRevokedBefore_IsAllowed()
    {
        _index.Add(new ServerCertificate { CommonName = "db1", Status = CertificateStatus.Revoked, IsRevoked = true, SerialNumber = "01" });

        await Create().CreateAsync(new LocalProfileCreateRequest { CommonName = "db1" }, CancellationToken.None);

        Assert.Single(_events);
    }

    [Fact]
    public async Task Create_WithoutRemoteOrPublicIp_AsksForRemote()
    {
        var ex = await Assert.ThrowsAsync<LocalIssuanceValidationException>(() =>
            Create(new()).CreateAsync(new LocalProfileCreateRequest { CommonName = "db1" }, CancellationToken.None));
        Assert.Contains("--remote", ex.Message);
    }

    [Fact]
    public async Task Create_RemoteWithInjection_IsRejected()
    {
        await Assert.ThrowsAsync<LocalIssuanceValidationException>(() =>
            Create().CreateAsync(new LocalProfileCreateRequest { CommonName = "db1", Remote = "evil\nup /bin/sh" }, CancellationToken.None));
    }

    [Fact]
    public async Task Revoke_LocalProfile_QueuesRevokedEventWithSameSerial()
    {
        var service = Create();
        await service.CreateAsync(new LocalProfileCreateRequest { CommonName = "db1", IsServiceProfile = true }, CancellationToken.None);

        await service.RevokeAsync("db1", CancellationToken.None);

        _ovpn.Verify(o => o.RevokeOvpnFile(It.IsAny<string>(), "db1", "db1.ovpn", "/x/db1.ovpn", It.IsAny<CancellationToken>()));
        Assert.Equal([("issued", "5E71A1"), ("revoked", "5E71A1")], _events.Select(e => (e.EventType, e.SerialNumber)));
        Assert.Null(_registry.FindActive("db1"));
    }

    [Fact]
    public async Task Revoke_WhenCertificateAlreadyGone_StillFreesStaticIp_AndQueuesEvent()
    {
        var service = Create();
        await service.CreateAsync(new LocalProfileCreateRequest { CommonName = "db1", IsServiceProfile = true }, CancellationToken.None);
        await _ccd.WriteAsync("db1", null, CancellationToken.None);
        _ovpn.Setup(o => o.RevokeOvpnFile(It.IsAny<string>(), "db1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("gone"));

        await service.RevokeAsync("db1", CancellationToken.None);

        Assert.False(File.Exists(_dir.Combine("ccd", "db1")));
        Assert.Equal("revoked", _events.Last().EventType);
    }

    [Fact]
    public async Task Revoke_DashboardIssuedCn_IsRefused()
    {
        await Assert.ThrowsAsync<LocalIssuanceValidationException>(() => Create().RevokeAsync("dashboard-user", CancellationToken.None));
        _ovpn.Verify(o => o.RevokeOvpnFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_MarksOriginFromRegistry_AndStatusFromIndex()
    {
        var service = Create();
        await service.CreateAsync(new LocalProfileCreateRequest { CommonName = "db1", IsServiceProfile = true, StaticIp = "10.50.28.201" }, CancellationToken.None);
        _index.Add(new ServerCertificate { CommonName = "db1", SerialNumber = "5E71A1", Status = CertificateStatus.Active });
        _index.Add(new ServerCertificate { CommonName = "alice", SerialNumber = "02", Status = CertificateStatus.Active });
        _index.Add(new ServerCertificate { CommonName = "server", SerialNumber = "00", Status = CertificateStatus.Active });

        var list = await service.ListAsync(CancellationToken.None);

        Assert.Equal(["alice", "db1"], list.Select(p => p.CommonName));
        Assert.Equal(("dashboard", false), (list[0].Origin, list[0].IsServiceProfile));
        Assert.Equal(("local", true, "10.50.28.201", "active"), (list[1].Origin, list[1].IsServiceProfile, list[1].StaticIp, list[1].Status));
    }

    [Fact]
    public async Task Disabled_RefusesEverything()
    {
        var disabled = new LocalProfileService(new LocalIssuanceOptions { Enabled = false, Directory = _dir.Combine("off") },
            _ovpn.Object, _easyRsa.Object, Mock.Of<IEasyRsaPathResolver>(), _registry, _outbox.Object, _ccd,
            new ConfigurationBuilder().Build(), TimeProvider.System, NullLogger<LocalProfileService>.Instance);

        await Assert.ThrowsAsync<LocalIssuanceValidationException>(() =>
            disabled.CreateAsync(new LocalProfileCreateRequest { CommonName = "db1" }, CancellationToken.None));
    }
}
