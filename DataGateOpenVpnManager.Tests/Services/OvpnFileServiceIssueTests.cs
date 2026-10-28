using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Cert.Responses;
using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services;
using DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;
using DataGateOpenVpnManager.Services.Interfaces;
using DataGateOpenVpnManager.Services.LocalIssuance;
using DataGateOpenVpnManager.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services;

/// <summary><see cref="OvpnFileService.IssueOvpnFile"/>: shared pipeline for dashboard and local issuance.</summary>
public class OvpnFileServiceIssueTests : IDisposable
{
    private const string Template = """
        setenv FRIENDLY_NAME "{{friendly_name}}"
        client
        dev tun
        proto udp
        remote {{server_ip}} {{server_port}}
        nobind
        <ca>
        {{ca_cert}}
        </ca>
        <cert>
        {{client_cert}}
        </cert>
        <key>
        {{client_key}}
        </key>
        <tls-crypt>
        {{tls_auth_key}}
        </tls-crypt>
        """;

    private readonly TempDir _dir = new();
    private readonly Mock<IEasyRsaService> _easyRsa = new();
    private readonly Mock<IOvpnIssuanceTracker> _tracker = new();
    private readonly string _certPem;

    public OvpnFileServiceIssueTests()
    {
        var lease = new Mock<IOvpnIssuanceLease>();
        lease.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _tracker.Setup(t => t.Begin(It.IsAny<string>())).Returns(lease.Object);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var cert = new CertificateRequest("CN=db1", key, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        _certPem = cert.ExportCertificatePem();

        var pki = Directory.CreateDirectory(_dir.Combine("easy-rsa", "pki")).FullName;
        File.WriteAllText(Path.Combine(pki, "ca.crt"), _certPem);
        File.WriteAllText(Path.Combine(pki, "ta.key"), "ta-key");
        File.WriteAllText(Path.Combine(pki, "db1.crt"), "Certificate:\n    Data: ...\n" + _certPem);
        File.WriteAllText(Path.Combine(pki, "db1.key"), key.ExportPkcs8PrivateKeyPem());

        _easyRsa.Setup(s => s.BuildCertificateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), "db1", It.IsAny<int>()))
            .ReturnsAsync(new ServerCertificate
            {
                CommonName = "db1",
                SerialNumber = "0A1B2C",
                CertificatePath = Path.Combine(pki, "db1.crt"),
                KeyPath = Path.Combine(pki, "db1.key"),
            });
    }

    public void Dispose() => _dir.Dispose();

    private (OvpnFileService Service, ClientConfigDirStore Ccd) Create(bool ccdEnabled = true, bool ccdExclusive = false)
    {
        var ccd = new ClientConfigDirStore(new LocalIssuanceOptions
        {
            ClientConfigDir = ccdEnabled ? _dir.Combine("ccd") : null,
            CcdExclusive = ccdExclusive,
            VpnSubnet = "10.50.28.0",
            VpnNetmask = "255.255.255.0",
            IppFile = _dir.Combine("ipp.txt"),
        }, NullLogger<ClientConfigDirStore>.Instance);
        var service = new OvpnFileService(Mock.Of<ILogger<IOvpnFileService>>(), _easyRsa.Object,
            Options.Create(new EasyRsaOptions()), _tracker.Object,
            new ConfigurationBuilder().AddInMemoryCollection().Build(), ccd);
        return (service, ccd);
    }

    private OvpnIssueRequest Request(bool service = false, string? staticIp = null) => new()
    {
        EasyRsaPath = _dir.Combine("easy-rsa"),
        CommonName = "db1",
        FriendlyName = "db1",
        ConfigTemplate = Template,
        ServerIp = "203.0.113.10",
        ServerPort = 1194,
        IsServiceProfile = service,
        StaticIp = staticIp,
    };

    [Fact]
    public async Task Issue_ServiceProfile_HasPullFilters_AndCcdWithStaticIp()
    {
        var (service, _) = Create();

        var result = await service.IssueOvpnFile(Request(service: true, staticIp: "10.50.28.201"), CancellationToken.None);

        var ovpn = await File.ReadAllTextAsync(result.Metadata.FilePath);
        Assert.Contains("pull-filter ignore \"redirect-gateway\"", ovpn);
        Assert.Contains("pull-filter ignore \"dhcp-option\"", ovpn);
        Assert.Contains("pull-filter ignore \"block-outside-dns\"", ovpn);
        Assert.Contains("remote 203.0.113.10 1194", ovpn);
        Assert.DoesNotContain("{{", ovpn);
        Assert.Equal(ClientConfigDirStore.ManagedHeader + "\nifconfig-push 10.50.28.201 255.255.255.0\n",
            await File.ReadAllTextAsync(_dir.Combine("ccd", "db1")));
        Assert.Equal("0A1B2C", result.Certificate.SerialNumber);
        Assert.Matches("^[0-9a-f]{64}$", result.CertFingerprintSha256);
    }

    [Fact]
    public async Task Issue_RegularProfile_IsUnchanged_NoPullFilter_NoCcd()
    {
        var (service, _) = Create();

        var result = await service.IssueOvpnFile(Request(), CancellationToken.None);

        Assert.DoesNotContain("pull-filter", await File.ReadAllTextAsync(result.Metadata.FilePath));
        Assert.False(Directory.Exists(_dir.Combine("ccd")));
    }

    [Fact]
    public async Task Issue_CcdExclusive_WritesCcdForEveryProfile()
    {
        var (service, _) = Create(ccdExclusive: true);

        await service.IssueOvpnFile(Request(), CancellationToken.None);

        Assert.Equal(ClientConfigDirStore.ManagedHeader + "\n", await File.ReadAllTextAsync(_dir.Combine("ccd", "db1")));
    }

    [Fact]
    public async Task Issue_StaticIpTakenByOtherCn_IsRejectedBeforeEasyRsa()
    {
        var (service, ccd) = Create();
        await ccd.WriteAsync("db2", "10.50.28.201", CancellationToken.None);

        var ex = await Assert.ThrowsAsync<LocalIssuanceValidationException>(() =>
            service.IssueOvpnFile(Request(service: true, staticIp: "10.50.28.201"), CancellationToken.None));

        Assert.True(ex.IsConflict);
        _easyRsa.Verify(s => s.BuildCertificateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        Assert.False(File.Exists(_dir.Combine("ccd", "db1")));
    }

    [Fact]
    public async Task Issue_StaticIpWithoutCcd_IsRejected()
    {
        var (service, _) = Create(ccdEnabled: false);

        await Assert.ThrowsAsync<LocalIssuanceValidationException>(() =>
            service.IssueOvpnFile(Request(service: true, staticIp: "10.50.28.201"), CancellationToken.None));
    }

    [Fact]
    public async Task Revoke_RemovesCcdFile()
    {
        var (service, ccd) = Create();
        await ccd.WriteAsync("db1", "10.50.28.201", CancellationToken.None);
        _easyRsa.Setup(s => s.RevokeCertificateAsync(It.IsAny<string>(), "db1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerCertificate());

        await service.RevokeOvpnFile(_dir.Combine("easy-rsa"), "db1", "db1.ovpn", _dir.Combine("none.ovpn"), CancellationToken.None);

        Assert.Empty(ccd.ReadStaticAssignments());
    }
}
