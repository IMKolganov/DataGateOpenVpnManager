using DataGateOpenVpnManager.Services.OpenVpnTelnet;
using DataGateOpenVpnManager.Tests.Services.OpenVpnTelnet.Fakes;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataGateOpenVpnManager.Tests.Services.OpenVpnTelnet;

public class CommandQueueConcurrencyTests
{
    /// <summary>
    /// Documents false "client not in management": overlapping waiters get responses FIFO,
    /// so the first caller may receive a snapshot that does not contain its peer yet.
    /// </summary>
    [Fact]
    public async Task FifoResponseMatching_WithOverlappingCommands_FirstWaiterCanGetWrongSnapshot()
    {
        var pending = new Queue<TaskCompletionSource<string>>();

        Task<string> SendCommand()
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Enqueue(tcs);
            return tcs.Task;
        }

        void Receive(string payload)
        {
            if (pending.TryDequeue(out var tcs))
                tcs.TrySetResult(payload);
        }

        var byteDebugSnapshot = SendCommand();
        var zombieSnapshot = SendCommand();

        Receive("CLIENT_LIST\tstatusgate\t127.0.0.1:11111\t10.51.16.2\nEND");
        Receive("CLIENT_LIST\tadg-77\t127.0.0.1:55059\t10.51.16.3\nEND");

        var firstResult = await byteDebugSnapshot;
        var secondResult = await zombieSnapshot;

        Assert.Contains("statusgate", firstResult);
        Assert.DoesNotContain("55059", firstResult);
        Assert.Contains("adg-77", secondResult);
        Assert.Contains("55059", secondResult);
    }

    [Fact]
    public async Task SendCommandAsync_ConcurrentCommands_ExecutesAtMostOneSendAtATime()
    {
        var telnet = new ConcurrentTrackingFakeTelnetClient();
        var queue = new CommandQueue(telnet, Mock.Of<ILogger<CommandQueue>>());
        const int commandCount = 40;

        var responder = Task.Run(async () =>
        {
            for (var i = 0; i < commandCount; i++)
            {
                while (telnet.SentCommands.Count <= i)
                    await Task.Delay(1);

                telnet.SimulateIncomingData($"marker-{i}\nEND");
            }
        });

        var commands = Enumerable.Range(0, commandCount)
            .Select(i => queue.SendCommandAsync($"status-{i}", CancellationToken.None, timeoutMs: 5000))
            .ToArray();

        var results = await Task.WhenAll(commands);
        await responder;

        Assert.Equal(1, telnet.MaxConcurrentSends);
        Assert.Equal(commandCount, telnet.SentCommands.Count);
        for (var i = 0; i < commandCount; i++)
            Assert.Contains($"marker-{i}", results[i]);
    }

    [Fact]
    public async Task SendCommandAsync_ConcurrentStatus3LikeWorkload_EachCallerGetsItsOwnResponse()
    {
        var telnet = new ConcurrentTrackingFakeTelnetClient();
        var queue = new CommandQueue(telnet, Mock.Of<ILogger<CommandQueue>>());
        const int commandCount = 20;

        var responder = Task.Run(async () =>
        {
            for (var i = 0; i < commandCount; i++)
            {
                while (telnet.SentCommands.Count <= i)
                    await Task.Delay(1);

                telnet.SimulateIncomingData(
                    $"CLIENT_LIST\tclient-{i}\t127.0.0.1:{50000 + i}\t10.51.16.{i + 2}\nEND");
            }
        });

        var tasks = Enumerable.Range(0, commandCount)
            .Select(_ => queue.SendCommandAsync("status 3", CancellationToken.None, timeoutMs: 5000))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        await responder;

        for (var i = 0; i < commandCount; i++)
            Assert.Contains($"client-{i}", results[i]);
    }

    [Fact]
    public async Task SendCommandAsync_LateResponseAfterTimeout_DoesNotPoisonNextCommand()
    {
        var telnet = new FakeTelnetClient();
        var queue = new CommandQueue(telnet, Mock.Of<ILogger<CommandQueue>>());

        await Assert.ThrowsAsync<TimeoutException>(() =>
            queue.SendCommandAsync("slow", CancellationToken.None, timeoutMs: 50));

        telnet.SimulateIncomingData("late-response\nEND");

        var second = queue.SendCommandAsync("fast", CancellationToken.None, timeoutMs: 5000);
        telnet.SimulateIncomingData("expected-response\nEND");

        var result = await second;
        Assert.Contains("expected-response", result);
        Assert.DoesNotContain("late-response", result);
    }

    private sealed class ConcurrentTrackingFakeTelnetClient : FakeTelnetClient
    {
        private int _concurrentSends;

        public int MaxConcurrentSends { get; private set; }

        public override Task SendAsync(string command, CancellationToken cancellationToken)
        {
            var inFlight = Interlocked.Increment(ref _concurrentSends);
            MaxConcurrentSends = Math.Max(MaxConcurrentSends, inFlight);
            try
            {
                return base.SendAsync(command, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentSends);
            }
        }
    }
}
