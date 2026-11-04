using DataGateMonitor.SharedModels.DataGateMonitor.LocalIssuance;
using DataGateMonitor.SharedModels.DataGateMonitor.LocalIssuance.Requests;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.LocalIssuance.Requests;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
    private readonly List<LocalIssuanceEventRequest> _events = [];
    private readonly List<ServerCertificate> _index = [];
    private readonly LocalProfileRegistry _registry;
    private readonly LocalIssuanceOptions _options;
    private OvpnIssueRequest? _lastIssue;
    private string _issuedSerial = "5E71A1";

    public LocalProfileServiceTests()
    {
        _options = new LocalIssuanceOptions { Enabled = true, Directory = _dir.Combine("li") };
        _registry = new LocalProfileRegistry(_options);
        Directory.CreateDirectory(_dir.Combine("easy-rsa", "pki", "issued"));

        _easyRsa.Setup(e => e.GetAllCertificateInfoInIndexFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _index.ToList());
        _outbox.Setup(o => o.EnqueueAsync(It.IsAny<LocalIssuanceEventRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LocalIssuanceEventRequest, CancellationToken>((e, _) => _events.Add(e))
            .Returns(Task.CompletedTask);
        _ovpn.Setup(o => o.IssueOvpnFile(It.IsAny<OvpnIssueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OvpnIssueRequest r, CancellationToken _) =>
            {
                _lastIssue = r;
                return new OvpnIssueResult(
                    new OvpnFileMetadata { CommonName = r.CommonName, FileName = $"{r.CommonName}.ovpn", FilePath = $"/x/{r.CommonName}.ovpn", IssuedTo = r.IssuedTo, CertFilePath = "c", KeyFilePath = "k" },
                    new ServerCertificate { CommonName = r.CommonName, SerialNumber = _issuedSerial, Status = CertificateStatus.Active },
                    new string('f', 64));
            });
    }

    public void Dispose() => _dir.Dispose();

    private void IndexEntry(string cn, string serial, bool revoked) => _index.Add(new ServerCertificate
    {
        CommonName = cn,
        SerialNumber = serial,
        IsRevoked = revoked,
        Status = revoked ? CertificateStatus.Revoked : CertificateStatus.Active,
    });

    /// <summary>pki/issued/{cn}.crt with exactly these DER serial bytes (as easy-rsa would leave it).</summary>
    private void WriteIssuedCert(string cn, string derSerialHex)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var name = new X500DistinguishedName($"CN={cn}");
        var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256);
        using var cert = request.Create(name, X509SignatureGenerator.CreateForECDsa(key),
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30), Convert.FromHexString(derSerialHex));
        File.WriteAllText(_dir.Combine("easy-rsa", "pki", "issued", $"{cn}.crt"), cert.ExportCertificatePem());
    }

    private void VerifyNeverRevokedInPki() =>
        _ovpn.Verify(o => o.RevokeOvpnFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);

    private LocalProfileService Create(Dictionary<string, string?>? config = null)
    {
        var paths = new Mock<IEasyRsaPathResolver>();
        paths.Setup(p => p.GetEasyRsaPath()).Returns(_dir.Combine("easy-rsa"));
        return new LocalProfileService(_options, _ovpn.Object, _easyRsa.Object, paths.Object, _registry, _outbox.Object,
            new ConfigurationBuilder().AddInMemoryCollection(config ?? new() { ["PUBLIC_IP"] = "203.0.113.10", ["PORT"] = "1295" }).Build(),
            TimeProvider.System, NullLogger<LocalProfileService>.Instance);
    }

    [Fact]
    public async Task Create_ServiceProfile_UsesSharedPipeline_RecordsAndQueuesEvent()
    {
        var view = await Create().CreateAsync(
            new LocalProfileCreateRequest { CommonName = "db1", IsServiceProfile = true },
            CancellationToken.None);

        Assert.NotNull(_lastIssue);
        Assert.True(_lastIssue!.IsServiceProfile);
        Assert.Equal(("203.0.113.10", 1295, LocalProfileService.IssuedToLocalService),
            (_lastIssue.ServerIp, _lastIssue.ServerPort, _lastIssue.IssuedTo));

        var evt = Assert.Single(_events);
        Assert.Equal((LocalIssuanceEventTypes.Issued, "local", "5E71A1", true, new string('f', 64)),
            (evt.EventType, evt.Origin, evt.SerialNumber, evt.IsServiceProfile, evt.CertFingerprintSha256));
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
        IndexEntry("db1", "5E71A1", revoked: false);
        WriteIssuedCert("db1", "5E71A1");

        await service.RevokeAsync("db1", CancellationToken.None);

        _ovpn.Verify(o => o.RevokeOvpnFile(It.IsAny<string>(), "db1", "db1.ovpn", "/x/db1.ovpn", It.IsAny<CancellationToken>()));
        Assert.Equal([("issued", "5E71A1"), ("revoked", "5E71A1")], _events.Select(e => (e.EventType, e.SerialNumber)));
        Assert.Null(_registry.FindActive("db1"));
    }

    // Serials as easy-rsa 3.1.0/3.1.7/3.2.7 actually produce them (index.txt form vs the DER bytes .NET reads).
    [Theory]
    [InlineData("F5A1C0FFEE0011223344556677889900", "00F5A1C0FFEE0011223344556677889900")] // high bit: .NET adds 00
    [InlineData("0CDE25A6AF8C33195EE2DBC6A53BD5", "0CDE25A6AF8C33195EE2DBC6A53BD5")] // 30 chars (3.1.x, leading 00 byte dropped)
    [InlineData("4BFB5D624672DB2A7B354E67F5881B9E", "4BFB5D624672DB2A7B354E67F5881B9E")]
    public async Task Revoke_MatchesIssuedCertificate_AcrossSerialFormats(string indexSerial, string derSerial)
    {
        _issuedSerial = indexSerial;
        var service = Create();
        await service.CreateAsync(new LocalProfileCreateRequest { CommonName = "db1" }, CancellationToken.None);
        IndexEntry("db1", indexSerial, revoked: false);
        WriteIssuedCert("db1", derSerial);

        await service.RevokeAsync("db1", CancellationToken.None);

        _ovpn.Verify(o => o.RevokeOvpnFile(It.IsAny<string>(), "db1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Revoke_AlreadyRevokedFromDashboard_RecordsIt_WithoutTouchingPki()
    {
        var service = Create();
        await service.CreateAsync(new LocalProfileCreateRequest { CommonName = "db1", IsServiceProfile = true }, CancellationToken.None);
        IndexEntry("db1", "5E71A1", revoked: true);

        var view = await service.RevokeAsync("db1", CancellationToken.None);

        VerifyNeverRevokedInPki();
        Assert.Equal("revoked", view.Status);
        Assert.Equal(("revoked", "5E71A1"), (_events.Last().EventType, _events.Last().SerialNumber));
        Assert.Null(_registry.FindActive("db1"));
    }

    [Fact]
    public async Task Revoke_AfterDashboardRevokedAndReissuedSameCn_LeavesTheNewCertificateAlone()
    {
        var service = Create();
        await service.CreateAsync(new LocalProfileCreateRequest { CommonName = "router" }, CancellationToken.None);
        // Dashboard revoked our 5E71A1, then issued "router" again: index.txt now lists the CN twice.
        IndexEntry("router", "5E71A1", revoked: true);
        IndexEntry("router", "C0FFEE", revoked: false);
        WriteIssuedCert("router", "C0FFEE");

        await service.RevokeAsync("router", CancellationToken.None);

        VerifyNeverRevokedInPki();
        Assert.Equal(("revoked", "5E71A1"), (_events.Last().EventType, _events.Last().SerialNumber));
    }

    [Theory]
    [InlineData("C0FFEE")]
    [InlineData(null)]
    public async Task Revoke_WhenIssuedFileIsNotThisCertificate_IsConflict_AndChangesNothing(string? issuedSerial)
    {
        var service = Create();
        await service.CreateAsync(new LocalProfileCreateRequest { CommonName = "db1" }, CancellationToken.None);
        IndexEntry("db1", "5E71A1", revoked: false);
        if (issuedSerial is not null)
            WriteIssuedCert("db1", issuedSerial);

        var ex = await Assert.ThrowsAsync<LocalIssuanceValidationException>(() => service.RevokeAsync("db1", CancellationToken.None));

        Assert.True(ex.IsConflict);
        VerifyNeverRevokedInPki();
        Assert.NotNull(_registry.FindActive("db1"));
        Assert.Single(_events);
    }

    [Fact]
    public async Task Revoke_SerialMissingFromIndex_IsConflict()
    {
        var service = Create();
        await service.CreateAsync(new LocalProfileCreateRequest { CommonName = "db1" }, CancellationToken.None);
        WriteIssuedCert("db1", "5E71A1");

        var ex = await Assert.ThrowsAsync<LocalIssuanceValidationException>(() => service.RevokeAsync("db1", CancellationToken.None));

        Assert.True(ex.IsConflict);
        VerifyNeverRevokedInPki();
    }

    [Fact]
    public async Task GetOvpn_RevokedFromDashboard_IsRefused()
    {
        var service = Create();
        await service.CreateAsync(new LocalProfileCreateRequest { CommonName = "db1" }, CancellationToken.None);
        IndexEntry("db1", "5E71A1", revoked: true);

        var ex = await Assert.ThrowsAsync<LocalIssuanceValidationException>(() => service.GetOvpnAsync("db1", CancellationToken.None));

        Assert.Contains("revoked", ex.Message);
    }

    [Fact]
    public async Task Revoke_DashboardIssuedCn_IsRefused()
    {
        await Assert.ThrowsAsync<LocalIssuanceValidationException>(() => Create().RevokeAsync("dashboard-user", CancellationToken.None));
        VerifyNeverRevokedInPki();
    }

    [Fact]
    public async Task List_MarksOriginFromRegistry_AndStatusFromIndex()
    {
        var service = Create();
        await service.CreateAsync(new LocalProfileCreateRequest { CommonName = "db1", IsServiceProfile = true }, CancellationToken.None);
        _index.Add(new ServerCertificate { CommonName = "db1", SerialNumber = "5E71A1", Status = CertificateStatus.Active });
        _index.Add(new ServerCertificate { CommonName = "alice", SerialNumber = "02", Status = CertificateStatus.Active });
        _index.Add(new ServerCertificate { CommonName = "server", SerialNumber = "00", Status = CertificateStatus.Active });

        var list = await service.ListAsync(CancellationToken.None);

        Assert.Equal(["alice", "db1"], list.Select(p => p.CommonName));
        Assert.Equal(("dashboard", false), (list[0].Origin, list[0].IsServiceProfile));
        Assert.Equal(("local", true, "active"), (list[1].Origin, list[1].IsServiceProfile, list[1].Status));
    }

    [Fact]
    public async Task Disabled_RefusesEverything()
    {
        var disabled = new LocalProfileService(new LocalIssuanceOptions { Enabled = false, Directory = _dir.Combine("off") },
            _ovpn.Object, _easyRsa.Object, Mock.Of<IEasyRsaPathResolver>(), _registry, _outbox.Object,
            new ConfigurationBuilder().Build(), TimeProvider.System, NullLogger<LocalProfileService>.Instance);

        await Assert.ThrowsAsync<LocalIssuanceValidationException>(() =>
            disabled.CreateAsync(new LocalProfileCreateRequest { CommonName = "db1" }, CancellationToken.None));
    }
}
