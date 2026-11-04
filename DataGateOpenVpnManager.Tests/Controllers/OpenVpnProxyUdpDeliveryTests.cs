using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading.Channels;
using DataGateOpenVpnManager.Controllers;
using DataGateOpenVpnManager.Services.Proxy;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataGateOpenVpnManager.Tests.Controllers;

public class OpenVpnProxyUdpDeliveryTests
{
    [Fact(Timeout = 10_000)]
    public async Task QueuedBurst_IsBatchedInOrder()
    {
        await using var pump = new UdpPump();
        var payloads = CreatePayloads(4, 1400);
        await pump.QueueAsync(payloads);

        pump.Start();
        var message = await pump.WebSocket.ReadMessageAsync();

        AssertPayloads(payloads, Decode(message));
        await pump.StopAsync();
        Assert.Equal(pump.Pool.Rented, pump.Pool.Returned);
    }

    [Theory(Timeout = 10_000)]
    [InlineData(96, 1400)]
    [InlineData(6, 48 * 1024)]
    public async Task SlowWebSocket_DeliversBurstWithoutDroppingDatagrams(int count, int size)
    {
        await using var pump = new UdpPump(blockSends: true);
        var payloads = CreatePayloads(count, size);
        await pump.QueueAsync(payloads);

        pump.Start();
        await pump.WebSocket.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        pump.WebSocket.ReleaseSends();

        var delivered = await pump.WebSocket.ReadDatagramsAsync(payloads.Length);
        AssertPayloads(payloads, delivered);
        await pump.StopAsync();
        Assert.Equal(pump.Pool.Rented, pump.Pool.Returned);
    }

    [Fact(Timeout = 10_000)]
    public async Task SingleDatagram_IsSentWithoutWaitingForAnotherDatagram()
    {
        await using var pump = new UdpPump();
        var payloads = CreatePayloads(1, 1400);
        pump.Start();

        await pump.QueueAsync(payloads);
        AssertPayloads(payloads, Decode(await pump.WebSocket.ReadMessageAsync()));

        await pump.StopAsync();
        Assert.Equal(pump.Pool.Rented, pump.Pool.Returned);
    }

    [Fact(Timeout = 10_000)]
    public async Task MixedPayloadSizes_FlushFullBatchWithoutCorruptingDatagrams()
    {
        await using var pump = new UdpPump();
        byte[][] payloads = [CreatePayload(0, 40_000), CreatePayload(1, 40_000), CreatePayload(2, 1000)];
        await pump.QueueAsync(payloads);
        pump.Start();

        var first = Decode(await pump.WebSocket.ReadMessageAsync());
        var second = Decode(await pump.WebSocket.ReadMessageAsync());

        AssertPayloads(payloads[..1], first);
        AssertPayloads(payloads[1..], second);
        await pump.StopAsync();
        Assert.Equal(pump.Pool.Rented, pump.Pool.Returned);
    }

    [Fact(Timeout = 10_000)]
    public async Task CancellationDuringBlockedSend_ReturnsAllRentedBuffers()
    {
        await using var pump = new UdpPump(blockSends: true);
        await pump.QueueAsync(CreatePayloads(6, 48 * 1024));
        pump.Start();
        await pump.WebSocket.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await pump.StopAsync();

        Assert.Equal(pump.Pool.Rented, pump.Pool.Returned);
    }

    [Fact(Timeout = 10_000)]
    public async Task WebSocketSendFailure_StopsReceiverAndReturnsAllRentedBuffers()
    {
        await using var pump = new UdpPump(failSends: true);
        await pump.QueueAsync(CreatePayloads(6, 48 * 1024));
        pump.Start();

        await pump.Completion.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(pump.Pool.Rented, pump.Pool.Returned);
    }

    private static byte[][] CreatePayloads(int count, int size) =>
        Enumerable.Range(0, count).Select(id => CreatePayload(id, size)).ToArray();

    private static byte[] CreatePayload(int id, int size)
    {
        var payload = Enumerable.Range(0, size).Select(i => (byte)(i % 251)).ToArray();
        BinaryPrimitives.WriteInt32BigEndian(payload, id);
        return payload;
    }

    private static List<byte[]> Decode(byte[] message)
    {
        var payloads = new List<byte[]>();
        var offset = 0;
        while (offset < message.Length)
        {
            var next = UdpWsFraming.TryParseNextFrame(message, offset, out var payload);
            Assert.True(next > offset, "WebSocket message contains an invalid UDP frame.");
            payloads.Add(payload.ToArray());
            offset = next;
        }
        return payloads;
    }

    private static void AssertPayloads(IReadOnlyList<byte[]> expected, IReadOnlyList<byte[]> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
            Assert.Equal(expected[i], actual[i]);
    }

    private sealed class UdpPump : IAsyncDisposable
    {
        private readonly Socket _source = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        private readonly Socket _receiver;
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(8));

        public UdpPump(bool blockSends = false, bool failSends = false)
        {
            _source.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            _receiver = OpenVpnProxyController.CreateVpnUdpSocket(
                (IPEndPoint)_source.LocalEndPoint!, NullLogger.Instance);
            _source.Connect(_receiver.LocalEndPoint!);
            WebSocket = new RecordingWebSocket(blockSends, failSends);
        }

        public ProxyBatchBufferPool Pool { get; } = new();
        public RecordingWebSocket WebSocket { get; }
        public Task Completion { get; private set; } = Task.CompletedTask;

        public async Task QueueAsync(IEnumerable<byte[]> payloads)
        {
            foreach (var payload in payloads)
                Assert.Equal(payload.Length, await _source.SendAsync(payload, SocketFlags.None, _lifetime.Token));
        }

        public void Start() => Completion = OpenVpnProxyController.PumpUdpToWebSocket(
            WebSocket, _receiver, _lifetime.Token, NullLogger.Instance, null, Pool);

        public async Task StopAsync()
        {
            await _lifetime.CancelAsync();
            await Completion.WaitAsync(TimeSpan.FromSeconds(3));
        }

        public async ValueTask DisposeAsync()
        {
            try { await StopAsync(); }
            finally
            {
                _receiver.Dispose();
                _source.Dispose();
                WebSocket.Dispose();
                _lifetime.Dispose();
            }
        }
    }

    private sealed class RecordingWebSocket : WebSocket
    {
        private readonly Channel<byte[]> _messages = Channel.CreateUnbounded<byte[]>();
        private readonly TaskCompletionSource _sendGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool _failSends;
        private WebSocketState _state = WebSocketState.Open;

        public RecordingWebSocket(bool blockSends, bool failSends)
        {
            _failSends = failSends;
            if (!blockSends)
                ReleaseSends();
        }

        public TaskCompletionSource SendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override WebSocketState State => _state;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;
        public void ReleaseSends() => _sendGate.TrySetResult();

        public async Task<byte[]> ReadMessageAsync() =>
            await _messages.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        public async Task<List<byte[]>> ReadDatagramsAsync(int count)
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var payloads = new List<byte[]>();
            try
            {
                while (payloads.Count < count)
                    payloads.AddRange(Decode(await _messages.Reader.ReadAsync(budget.Token)));
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested) { }
            return payloads;
        }

        public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType,
            bool endOfMessage, CancellationToken cancellationToken)
        {
            SendStarted.TrySetResult();
            await _sendGate.Task.WaitAsync(cancellationToken);
            if (_failSends)
                throw new WebSocketException("Simulated send failure");
            await _messages.Writer.WriteAsync(buffer.ToArray(), cancellationToken);
        }

        public override void Abort() => _state = WebSocketState.Aborted;
        public override void Dispose() => _state = WebSocketState.Closed;
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
