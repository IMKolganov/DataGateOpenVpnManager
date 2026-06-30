using System.Collections.Concurrent;
using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.OpenVpnTls;
using Microsoft.Extensions.Options;

namespace DataGateOpenVpnManager.Services.OpenVpnTls;

public sealed class OpenVpnTlsLogEnrichmentHostedService(
    IConfiguration configuration,
    IOptions<OpenVpnProxyOptions> proxyOptions,
    IOpenVpnTlsErrorClassifier classifier,
    ILogger<OpenVpnTlsLogEnrichmentHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastEmittedUtc = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!proxyOptions.Value.TlsLogEnrichmentEnabled)
        {
            logger.LogInformation("OpenVPN TLS log enrichment is disabled.");
            return;
        }

        var logPath = ResolveLogPath();
        logger.LogInformation("OpenVPN TLS log enrichment watching {LogPath}", logPath);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!File.Exists(logPath))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                await TailFileAsync(logPath, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OpenVPN TLS log enrichment failed; retrying in 5s.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task TailFileAsync(string logPath, CancellationToken stoppingToken)
    {
        await using var stream = new FileStream(
            logPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        using var reader = new StreamReader(stream);
        stream.Seek(0, SeekOrigin.End);

        while (!stoppingToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(stoppingToken);
            if (line is null)
            {
                if (stream.Length < stream.Position)
                    stream.Seek(0, SeekOrigin.Begin);

                await Task.Delay(PollInterval, stoppingToken);
                continue;
            }

            if (!classifier.IsTlsCryptLine(line))
                continue;

            var context = classifier.Classify(line);
            if (!ShouldEmit(context))
                continue;

            Emit(context);
        }
    }

    private bool ShouldEmit(OpenVpnTlsErrorContext context)
    {
        var key = $"{(int)context.Origin}|{context.Peer}|{context.ClientRef}|{context.ConnectionId}";
        var now = DateTimeOffset.UtcNow;
        if (_lastEmittedUtc.TryGetValue(key, out var last) && now - last < DedupWindow)
            return false;

        _lastEmittedUtc[key] = now;
        return true;
    }

    private void Emit(OpenVpnTlsErrorContext context)
    {
        switch (context.Origin)
        {
            case OpenVpnTlsErrorOrigin.ExternalProbe:
                logger.LogInformation(
                    "[OpenVpnTlsExternalProbe] peer={Peer} message={Message}",
                    context.Peer ?? "-",
                    TrimMessage(context.RawLine));
                break;

            case OpenVpnTlsErrorOrigin.AppViaProxy:
                logger.LogWarning(
                    "[OpenVpnTlsAppClient] peer={Peer} localPort={LocalProxyPort} connectionId={ConnectionId} " +
                    "clientRef={ClientRef} userId={UserId} username={Username} email={Email} " +
                    "realClient={RealClient} userAgent={UserAgent} message={Message}",
                    context.Peer ?? "-",
                    context.LocalProxyPort,
                    context.ConnectionId ?? "-",
                    context.ClientRef ?? "-",
                    context.UserId ?? "-",
                    context.Username ?? "-",
                    context.Email ?? "-",
                    FormatRealClient(context.RealClientIp, context.RealClientPort),
                    context.UserAgent ?? "-",
                    TrimMessage(context.RawLine));
                break;

            default:
                logger.LogWarning(
                    "[OpenVpnTlsLocalUnknown] peer={Peer} localPort={LocalProxyPort} message={Message}",
                    context.Peer ?? "-",
                    context.LocalProxyPort,
                    TrimMessage(context.RawLine));
                break;
        }
    }

    private string ResolveLogPath()
    {
        var dataDir = configuration["DATA_DIR"] ?? "/mnt";
        return Path.Combine(dataDir, "openvpn.log");
    }

    private static string FormatRealClient(string? ip, int port) =>
        string.IsNullOrWhiteSpace(ip) ? "-" : port > 0 ? $"{ip}:{port}" : ip;

    private static string TrimMessage(string line) =>
        line.Length <= 500 ? line : line[..500] + "...";
}
