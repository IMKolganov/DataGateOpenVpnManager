using DataGateMonitor.SharedModels.DataGateMonitor.LocalIssuance;
using DataGateMonitor.SharedModels.DataGateMonitor.LocalIssuance.Requests;
using System.Net;
using System.Security.Cryptography;
using DataGateOpenVpnManager.Services.LocalIssuance;
using DataGateOpenVpnManager.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;

namespace DataGateOpenVpnManager.Tests.Services.LocalIssuance;

public class LocalIssuanceEventDeliveryTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly LocalIssuanceOptions _options;
    private readonly NodeIdentity _identity;

    public LocalIssuanceEventDeliveryTests()
    {
        _options = new LocalIssuanceOptions { Enabled = true, Directory = _dir.Combine("li") };
        _identity = new NodeIdentity(_options, NullLogger<NodeIdentity>.Instance);
    }

    public void Dispose()
    {
        _identity.Dispose();
        _dir.Dispose();
    }

    private LocalIssuanceOutbox NewOutbox() => new(_options, NullLogger<LocalIssuanceOutbox>.Instance);

    private static LocalIssuanceEventRequest Issued(string serial, string cn = "db1") => new()
    {
        EventId = Guid.NewGuid(),
        Origin = "local",
        EventType = LocalIssuanceEventTypes.Issued,
        CommonName = cn,
        SerialNumber = serial,
        CertFingerprintSha256 = new string('a', 64),
        IsServiceProfile = true,
        OccurredAt = DateTimeOffset.UtcNow,
    };

    private LocalIssuanceEventDeliveryService NewDelivery(ILocalIssuanceOutbox outbox, FakeBackend backend)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(LocalIssuanceEventDeliveryService.HttpClientName))
            .Returns(() => new HttpClient(backend, disposeHandler: false) { BaseAddress = new Uri("http://backend.test/") });
        return new LocalIssuanceEventDeliveryService(factory.Object, outbox, _identity, TimeProvider.System,
            NullLogger<LocalIssuanceEventDeliveryService>.Instance);
    }

    [Fact]
    public async Task ExistingOutboxJson_WithStaticIp_IsStillDelivered()
    {
        var outbox = NewOutbox();
        await outbox.EnqueueAsync(Issued("0A01"), default);
        var path = Assert.Single(outbox.ListPending()).Path;
        var json = Newtonsoft.Json.Linq.JObject.Parse(await File.ReadAllTextAsync(path));
        json["StaticIp"] = "10.50.28.201";
        await File.WriteAllTextAsync(path, json.ToString());
        var backend = new FakeBackend(HttpStatusCode.OK);
        Assert.True(await NewDelivery(outbox, backend).DeliverPendingAsync(default));
        Assert.Single(backend.Requests);
        Assert.Empty(outbox.ListPending());
    }

    [Fact]
    public async Task Deliver_PostsSignedEvent_AndRemovesItFromDisk()
    {
        var outbox = NewOutbox();
        await outbox.EnqueueAsync(Issued("0A01"), CancellationToken.None);
        var backend = new FakeBackend(HttpStatusCode.OK);

        Assert.True(await NewDelivery(outbox, backend).DeliverPendingAsync(CancellationToken.None));

        var request = Assert.Single(backend.Requests);
        Assert.Equal("/api/open-vpn-servers/local-issuance-events", request.Path);
        Assert.Equal(_identity.Fingerprint, request.Headers[NodeRequestSignature.KeyIdHeader]);
        var evt = JsonConvert.DeserializeObject<LocalIssuanceEventRequest>(request.Body)!;
        Assert.Equal(("issued", "local", "0A01", "db1", true),
            (evt.EventType, evt.Origin, evt.SerialNumber, evt.CommonName, evt.IsServiceProfile));

        // Signature verifies with the public key the node announces.
        using var publicKey = ECDsa.Create();
        publicKey.ImportFromPem(_identity.PublicKeyPem);
        var canonical = NodeRequestSignature.CanonicalBytes(long.Parse(request.Headers[NodeRequestSignature.TimestampHeader]),
            "POST", request.Path, System.Text.Encoding.UTF8.GetBytes(request.Body));
        Assert.True(publicKey.VerifyData(canonical, Convert.FromBase64String(request.Headers[NodeRequestSignature.SignatureHeader]),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        Assert.Empty(outbox.ListPending());
    }

    [Fact]
    public async Task BackendDown_EventStaysOnDisk_SurvivesRestart_AndIsDeliveredLater()
    {
        var outbox = NewOutbox();
        await outbox.EnqueueAsync(Issued("0A02"), CancellationToken.None);

        Assert.False(await NewDelivery(outbox, FakeBackend.Unreachable()).DeliverPendingAsync(CancellationToken.None));
        Assert.False(await NewDelivery(outbox, new FakeBackend(HttpStatusCode.ServiceUnavailable)).DeliverPendingAsync(CancellationToken.None));
        Assert.False(await NewDelivery(outbox, new FakeBackend(HttpStatusCode.Unauthorized)).DeliverPendingAsync(CancellationToken.None));
        Assert.Single(outbox.ListPending());

        // "Container restart": a fresh outbox over the same directory.
        var restarted = NewOutbox();
        var backend = new FakeBackend(HttpStatusCode.OK);
        Assert.True(await NewDelivery(restarted, backend).DeliverPendingAsync(CancellationToken.None));

        Assert.Equal("0A02", JsonConvert.DeserializeObject<LocalIssuanceEventRequest>(Assert.Single(backend.Requests).Body)!.SerialNumber);
        Assert.Empty(restarted.ListPending());
    }

    [Fact]
    public async Task Deliver_KeepsOrder_StopsAtFirstFailure()
    {
        var outbox = NewOutbox();
        await outbox.EnqueueAsync(Issued("0A03"), CancellationToken.None);
        await outbox.EnqueueAsync(new LocalIssuanceEventRequest { EventId = Guid.NewGuid(), Origin = "local", EventType = LocalIssuanceEventTypes.Revoked, SerialNumber = "0A03", CommonName = "db1" },
            CancellationToken.None);
        var backend = new FakeBackend(HttpStatusCode.InternalServerError);

        await NewDelivery(outbox, backend).DeliverPendingAsync(CancellationToken.None);

        Assert.Single(backend.Requests); // the revoke was not sent ahead of its issue
        Assert.Equal(["issued", "revoked"], outbox.ListPending().Select(i => i.Event.EventType));
    }

    [Fact]
    public async Task Enqueue_SameSerialAndType_IsQueuedOnce()
    {
        var outbox = NewOutbox();

        await outbox.EnqueueAsync(Issued("0A04"), CancellationToken.None);
        await outbox.EnqueueAsync(Issued("0A04"), CancellationToken.None);

        Assert.Single(outbox.ListPending());
    }

    [Fact]
    public async Task MissingBackendBaseUrl_KeepsEventInOutbox()
    {
        var outbox = NewOutbox();
        await outbox.EnqueueAsync(Issued("0A07"), CancellationToken.None);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(LocalIssuanceEventDeliveryService.HttpClientName))
            .Returns(() => new HttpClient());
        var delivery = new LocalIssuanceEventDeliveryService(
            factory.Object, outbox, _identity, TimeProvider.System,
            NullLogger<LocalIssuanceEventDeliveryService>.Instance);

        Assert.False(await delivery.DeliverPendingAsync(CancellationToken.None));
        Assert.Equal("0A07", Assert.Single(outbox.ListPending()).Event.SerialNumber);
    }

    [Fact]
    public async Task BackendRejectsAsMalformed_EventMovedAside_NotLost()
    {
        var outbox = NewOutbox();
        await outbox.EnqueueAsync(Issued("0A05"), CancellationToken.None);

        Assert.True(await NewDelivery(outbox, new FakeBackend(HttpStatusCode.BadRequest)).DeliverPendingAsync(CancellationToken.None));

        Assert.Empty(outbox.ListPending());
        Assert.Contains(Directory.GetFiles(_options.RejectedDir), f => f.EndsWith("-issued-0A05.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OutboxFiles_AreOwnerOnly()
    {
        if (!OperatingSystem.IsLinux())
            return;
        var outbox = NewOutbox();
        await outbox.EnqueueAsync(Issued("0A06"), CancellationToken.None);

        var file = Assert.Single(outbox.ListPending()).Path;
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(_options.NodeKeyPath));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(_options.Directory));
    }

    [Fact]
    public void NodeIdentity_IsStableAcrossRestarts()
    {
        using var again = new NodeIdentity(_options, NullLogger<NodeIdentity>.Instance);
        Assert.Equal(_identity.Fingerprint, again.Fingerprint);
        Assert.Equal(_identity.PublicKeyPem, again.PublicKeyPem);
    }

    private sealed record CapturedRequest(string Path, Dictionary<string, string> Headers, string Body);

    private sealed class FakeBackend(HttpStatusCode status, bool unreachable = false) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        public static FakeBackend Unreachable() => new(HttpStatusCode.OK, unreachable: true);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (unreachable)
                throw new HttpRequestException("Connection refused");

            Requests.Add(new CapturedRequest(
                request.RequestUri!.AbsolutePath,
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)),
                await request.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(status) { Content = new StringContent("{}") };
        }
    }
}
