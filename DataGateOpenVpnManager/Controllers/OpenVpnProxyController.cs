using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Mvc;

namespace DataGateOpenVpnManager.Controllers;

[ApiController]
[Route("api/proxy")]
public class OpenVpnProxyController(
    IConfiguration config,
    ILogger<OpenVpnProxyController> logger)
    : ControllerBase
{
    private const int MaxUdpDatagramSize = 64 * 1024;
    private const int WsSegmentSize = 16 * 1024;

    [HttpGet]
    public async Task Get([FromQuery] string? mode = null)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await HttpContext.Response.WriteAsync("WebSocket request required");
            return;
        }

        var portRaw = config["PORT"];
        if (!int.TryParse(portRaw, out var vpnPort) || vpnPort <= 0 || vpnPort > 65535)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await HttpContext.Response.WriteAsync("VPN port is not configured");
            return;
        }

        const string targetHost = "127.0.0.1";
        var targetIp = IPAddress.Loopback;

        using var ws = await HttpContext.WebSockets.AcceptWebSocketAsync();

        var ct = HttpContext.RequestAborted;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var modeNorm = (mode ?? "tcp").Trim().ToLowerInvariant();

        try
        {
            if (modeNorm == "udp")
                await HandleUdp(ws, targetIp, vpnPort, linkedCts.Token, logger);
            else
                await HandleTcp(ws, targetHost, vpnPort, linkedCts.Token, logger);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Proxy failed. mode={Mode} {Message}", modeNorm, e.Message);
        }

        try
        {
            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "WebSocket close error. {Message}", e.Message);
        }
    }

    private static async Task HandleTcp(
        WebSocket ws,
        string targetHost,
        int vpnPort,
        CancellationToken ct,
        ILogger logger)
    {
        using var tcp = new TcpClient();
        tcp.NoDelay = true;

        try
        {
            await tcp.ConnectAsync(targetHost, vpnPort, ct);
        }
        catch (Exception e)
        {
            logger.LogError(e, "TCP connect failed. {Host}:{Port}. {Message}", targetHost, vpnPort, e.Message);
            await TryCloseWs(ws, "TCP connect failed", logger);
            return;
        }

        await using var tcpStream = tcp.GetStream();

        var wsToTcp = PumpWebSocketToTcp(ws, tcpStream, ct, logger);
        var tcpToWs = PumpTcpToWebSocket(ws, tcpStream, ct, logger);

        await Task.WhenAny(wsToTcp, tcpToWs);
    }

    private static async Task HandleUdp(
        WebSocket ws,
        IPAddress targetIp,
        int vpnPort,
        CancellationToken ct,
        ILogger logger)
    {
        var remote = new IPEndPoint(targetIp, vpnPort);

        using var udp = new UdpClient(0);

        // Helps with bursts; adjust if you see memory pressure.
        udp.Client.SendBufferSize = 4 * 1024 * 1024;
        udp.Client.ReceiveBufferSize = 4 * 1024 * 1024;

        try
        {
            udp.Connect(remote);
        }
        catch (Exception e)
        {
            logger.LogError(e, "UDP connect failed. {Host}:{Port}. {Message}", remote.Address, remote.Port, e.Message);
            await TryCloseWs(ws, "UDP connect failed", logger);
            return;
        }

        logger.LogInformation("UDP proxy started. remote={Remote} local={Local}",
            remote, udp.Client.LocalEndPoint);

        // WS -> UDP (supports WS fragmentation)
        var wsToUdp = Task.Run(async () =>
        {
            var segment = new byte[WsSegmentSize];
            var datagram = new byte[MaxUdpDatagramSize];

            try
            {
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    var total = 0;

                    while (true)
                    {
                        var result = await ws.ReceiveAsync(segment, ct);

                        if (result.MessageType == WebSocketMessageType.Close)
                            return;

                        if (result.MessageType != WebSocketMessageType.Binary)
                        {
                            // Drain remainder of this message (if fragmented), then ignore it.
                            while (!result.EndOfMessage)
                                result = await ws.ReceiveAsync(segment, ct);

                            total = 0;
                            break;
                        }

                        if (result.Count > 0)
                        {
                            if (total + result.Count > datagram.Length)
                            {
                                logger.LogWarning("WS binary message exceeds UDP max size ({Max}). Dropping.", MaxUdpDatagramSize);

                                while (!result.EndOfMessage)
                                    result = await ws.ReceiveAsync(segment, ct);

                                total = 0;
                                break;
                            }

                            Buffer.BlockCopy(segment, 0, datagram, total, result.Count);
                            total += result.Count;
                        }

                        if (result.EndOfMessage)
                            break;
                    }

                    if (total > 0)
                        await udp.SendAsync(datagram.AsMemory(0, total), ct);
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "WS->UDP pump error. {Message}", e.Message);
            }
        }, ct);

        // UDP -> WS
        var udpToWs = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    var pkt = await udp.ReceiveAsync(ct);
                    if (pkt.Buffer.Length <= 0)
                        continue;

                    await ws.SendAsync(
                        pkt.Buffer,
                        WebSocketMessageType.Binary,
                        endOfMessage: true,
                        cancellationToken: ct
                    );
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (SocketException e)
            {
                logger.LogDebug(e, "UDP receive error. {Message}", e.Message);
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "UDP->WS pump error. {Message}", e.Message);
            }
        }, ct);

        await Task.WhenAny(wsToUdp, udpToWs);

        logger.LogInformation("UDP proxy stopped. remote={Remote}", remote);
    }

    private static async Task TryCloseWs(WebSocket ws, string reason, ILogger logger)
    {
        try
        {
            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.InternalServerError, reason, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogTrace("WebSocket close failed. {Message}", ex.Message);
        }
    }

    private static async Task PumpWebSocketToTcp(
        WebSocket ws,
        NetworkStream tcp,
        CancellationToken ct,
        ILogger logger)
    {
        var buffer = new byte[16 * 1024];

        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, ct);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType != WebSocketMessageType.Binary)
                    continue;

                await tcp.WriteAsync(buffer.AsMemory(0, result.Count), ct);

                while (!result.EndOfMessage)
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType != WebSocketMessageType.Binary)
                        break;

                    await tcp.WriteAsync(buffer.AsMemory(0, result.Count), ct);
                }

                // NetworkStream flush is typically unnecessary and may hurt throughput/latency.
                // await tcp.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogTrace("Connection cancelled. {Message}", ex.Message);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "WS->TCP pump error. {Message}", e.Message);
        }
    }

    private static async Task PumpTcpToWebSocket(
        WebSocket ws,
        NetworkStream tcp,
        CancellationToken ct,
        ILogger logger)
    {
        var buffer = new byte[16 * 1024];

        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var read = await tcp.ReadAsync(buffer, ct);
                if (read <= 0)
                    break;

                await ws.SendAsync(
                    buffer.AsMemory(0, read),
                    WebSocketMessageType.Binary,
                    endOfMessage: true,
                    cancellationToken: ct
                );
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogTrace("Connection cancelled. {Message}", ex.Message);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "TCP->WS pump error. {Message}", e.Message);
        }
    }
}