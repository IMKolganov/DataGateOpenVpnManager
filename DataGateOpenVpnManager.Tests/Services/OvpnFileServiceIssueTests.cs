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

    private OvpnFileService Create() => new(Mock.Of<ILogger<IOvpnFileService>>(), _easyRsa.Object,
        Options.Create(new EasyRsaOptions()), _tracker.Object,
        new ConfigurationBuilder().AddInMemoryCollection().Build());

    private OvpnIssueRequest Request(bool service = false) => new()
    {
        EasyRsaPath = _dir.Combine("easy-rsa"),
        CommonName = "db1",
        FriendlyName = "db1",
        ConfigTemplate = Template,
        ServerIp = "203.0.113.10",
        ServerPort = 1194,
        IsServiceProfile = service,
    };

    [Fact]
    public async Task Issue_ServiceProfile_HasPullFilters()
    {
        var service = Create();

        var result = await service.IssueOvpnFile(Request(service: true), CancellationToken.None);

        var ovpn = await File.ReadAllTextAsync(result.Metadata.FilePath);
        Assert.Contains("pull-filter ignore \"redirect-gateway\"", ovpn);
        Assert.Contains("pull-filter ignore \"dhcp-option\"", ovpn);
        Assert.Contains("pull-filter ignore \"block-outside-dns\"", ovpn);
        Assert.Contains("remote 203.0.113.10 1194", ovpn);
        Assert.DoesNotContain("{{", ovpn);
        Assert.Equal("0A1B2C", result.Certificate.SerialNumber);
        Assert.Matches("^[0-9a-f]{64}$", result.CertFingerprintSha256);
    }

    [Fact]
    public async Task Issue_RegularProfile_IsUnchanged_NoPullFilter()
    {
        var service = Create();

        var result = await service.IssueOvpnFile(Request(), CancellationToken.None);

        Assert.DoesNotContain("pull-filter", await File.ReadAllTextAsync(result.Metadata.FilePath));
    }

}
