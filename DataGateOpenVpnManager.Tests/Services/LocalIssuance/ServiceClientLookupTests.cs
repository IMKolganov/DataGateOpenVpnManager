using DataGateMonitor.SharedModels.DataGateOpenVpnManager.LocalIssuance.Responses;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Cert.Responses;
using DataGateMonitor.SharedModels.Enums;
using DataGateOpenVpnManager.Helpers;
using DataGateOpenVpnManager.Services.EasyRsaServices;
using DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;
using DataGateOpenVpnManager.Services.Interfaces;
using DataGateOpenVpnManager.Services.LocalIssuance;
using DataGateOpenVpnManager.Services.Proxy;
using DataGateOpenVpnManager.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services.LocalIssuance;

public class ServiceClientLookupTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly Mock<IOpenVpnManagementStatusCache> _status = new();
    private readonly Mock<IEasyRsaParseDbService> _easyRsa = new();
    private readonly LocalProfileRegistry _registry;
    private readonly List<ServerCertificate> _certificates = [];
    private IReadOnlyList<OpenVpnManagementClientEntry> _clients = [];
    private OpenVpnManagementStatusSnapshot? _snapshot;
    private bool _refreshSucceeds = true;
    private readonly long _connected = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public ServiceClientLookupTests()
    {
        _registry = new(new LocalIssuanceOptions { Directory = _dir.Combine("li") });
        _status.Setup(s => s.RefreshAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            if (_refreshSucceeds)
                _snapshot = new(DateTime.UtcNow, "END", _clients, true);
            return Task.CompletedTask;
        });
        _status.Setup(s => s.GetSnapshot()).Returns(() => _snapshot);
        _easyRsa.Setup(s => s.ParseCertificateInfoInIndexFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _certificates.ToList());
        WriteCrl();
    }

    public void Dispose() => _dir.Dispose();

    private void WriteCrl(bool revoked = false, string crlSerial = "5E71A1")
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=test-ca", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.KeyCertSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var ca = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var crl = new CertificateRevocationListBuilder();
        if (revoked)
            crl.AddEntry(Convert.FromHexString(crlSerial));
        Directory.CreateDirectory(_dir.Combine("pki"));
        File.WriteAllText(_dir.Combine("pki", "crl.pem"), PemEncoding.WriteString("X509 CRL",
            crl.Build(ca, 1, DateTimeOffset.UtcNow.AddDays(1), HashAlgorithmName.SHA256)));
    }

    [Theory]
    [InlineData("008071A1", "8071A1")]
    [InlineData("008071A1", "00:80:71:A1")]
    [InlineData("7F71A1", "007F71A1")]
    public async Task CrlDerSerialSignByte_DoesNotExposeRevokedClient(string crlSerial, string registrySerial)
    {
        AddClient(serial: registrySerial);
        var sut = Create();
        Assert.NotNull(await sut.FindAsync("10.50.28.2", default));
        WriteCrl(revoked: true, crlSerial: crlSerial);

        Assert.Null(await sut.FindAsync("10.50.28.2", default));
    }

    private void AddClient(bool service = true, string serial = "5E71A1", long? connected = null)
    {
        _registry.Upsert(new LocalProfileRecord { CommonName = "db1", SerialNumber = serial,
            IsServiceProfile = service, IssuedAt = DateTimeOffset.UtcNow.AddHours(-1) });
        _certificates.Add(new ServerCertificate { CommonName = "db1", SerialNumber = serial,
            Status = CertificateStatus.Active, ExpiryDate = DateTimeOffset.UtcNow.AddDays(1) });
        _clients = [new("db1", "203.0.113.4:12000", "10.50.28.2", 12, 34, connected ?? _connected)];
    }

    private ServiceClientLookup Create()
    {
        var paths = new Mock<IEasyRsaPathResolver>();
        paths.Setup(p => p.GetEasyRsaPath()).Returns(_dir.Path);
        return new(_status.Object, _registry, _easyRsa.Object, new EasyRsaPkiMutex(), paths.Object, NullLogger<ServiceClientLookup>.Instance);
    }

    [Fact]
    public async Task ServiceClient_IsFound_WithCurrentSessionFields_ThenDisappearsAfterDisconnect()
    {
        AddClient();
        var sut = Create();
        var result = await sut.FindAsync("10.50.28.2", default);
        Assert.Equal(new ServiceClientView("db1", "5E71A1", "10.50.28.2", "203.0.113.4:12000",
            DateTimeOffset.FromUnixTimeSeconds(_connected)), result);
        _clients = [];
        Assert.Null(await sut.FindAsync("10.50.28.2", default));
    }

    [Fact]
    public async Task UserClient_NeverFound_EvenWhenConnected()
    {
        AddClient(service: false);
        Assert.Null(await Create().FindAsync("10.50.28.2", default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Revoke_InCrlOrIndex_HidesStillConnectedClient(bool crl)
    {
        AddClient();
        var sut = Create();
        Assert.NotNull(await sut.FindAsync("10.50.28.2", default));
        if (crl)
            WriteCrl(revoked: true);
        else
            _certificates[0].IsRevoked = true;
        Assert.Null(await sut.FindAsync("10.50.28.2", default));
    }

    [Fact]
    public async Task Revoke_InRegistry_HidesStillConnectedClient()
    {
        AddClient();
        var record = _registry.FindActive("db1")!;
        record.RevokedAt = DateTimeOffset.UtcNow;
        _registry.Upsert(record);
        Assert.Null(await Create().FindAsync("10.50.28.2", default));
    }

    [Fact]
    public async Task ManagementFailure_NeverUsesPreviouslySuccessfulSnapshot()
    {
        AddClient();
        var sut = Create();
        Assert.NotNull(await sut.FindAsync("10.50.28.2", default));
        _refreshSucceeds = false;
        Assert.Null(await sut.FindAsync("10.50.28.2", default));
    }

    [Fact]
    public async Task MissingCrl_HidesClient()
    {
        AddClient();
        File.Delete(_dir.Combine("pki", "crl.pem"));
        Assert.Null(await Create().FindAsync("10.50.28.2", default));
    }

    [Fact]
    public async Task OldSession_CannotBecomeAReissuedServiceCertificate()
    {
        AddClient(connected: DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds());
        Assert.Null(await Create().FindAsync("10.50.28.2", default));
    }

    [Fact]
    public async Task SameCnReissuedAsUser_CannotInheritServiceFlag()
    {
        AddClient();
        _certificates[0].SerialNumber = "AAAA";
        Assert.Null(await Create().FindAsync("10.50.28.2", default));
    }
}
