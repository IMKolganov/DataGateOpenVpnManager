using System.Text;
using DataGateOpenVpnManager.Services.LocalIssuance;

namespace DataGateOpenVpnManager.Tests.Services.LocalIssuance;

public class NodeRequestSignatureTests
{
    [Fact]
    public void CanonicalBytes_MatchBackendFormat()
    {
        // Same vector is asserted in DataGateMonitor.Tests (LocalIssuanceEventServiceTests) — keep both in sync.
        var canonical = Encoding.UTF8.GetString(
            NodeRequestSignature.CanonicalBytes(1700000000, "post", "/api/open-vpn-servers/local-issuance-events", "{}"u8.ToArray()));

        Assert.Equal("DGN1\n1700000000\nPOST\n/api/open-vpn-servers/local-issuance-events\n"
                     + "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a", canonical);
    }

    [Fact]
    public void Apply_SignsRoutePath_NotProxyPrefixedUrl()
    {
        using var dir = new TestSupport.TempDir();
        using var identity = new NodeIdentity(new LocalIssuanceOptions { Directory = dir.Combine("li") },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NodeIdentity>.Instance);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://dash.example/backend/api/open-vpn-servers/local-issuance-events");
        var body = "{}"u8.ToArray();

        NodeRequestSignature.Apply(request, identity, body, DateTimeOffset.FromUnixTimeSeconds(1700000000),
            LocalIssuanceEventDeliveryService.EventsPath);

        using var key = System.Security.Cryptography.ECDsa.Create();
        key.ImportFromPem(identity.PublicKeyPem);
        var signature = Convert.FromBase64String(request.Headers.GetValues(NodeRequestSignature.SignatureHeader).Single());
        Assert.True(key.VerifyData(
            NodeRequestSignature.CanonicalBytes(1700000000, "POST", "/api/open-vpn-servers/local-issuance-events", body),
            signature, System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.Equal("1700000000", request.Headers.GetValues(NodeRequestSignature.TimestampHeader).Single());
    }
}
