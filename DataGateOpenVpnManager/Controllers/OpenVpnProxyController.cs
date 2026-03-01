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
        try
        {
            udp.Connect(remote);
            logger.LogInformation("UDP proxy started. remote={Remote} local={Local}", remote, udp.Client.LocalEndPoint);
        }
        catch (Exception e)
        {
            logger.LogError(e, "UDP connect failed. {Host}:{Port}. {Message}", remote.Address, remote.Port, e.Message);
            if (ws.State == WebSocketState.Open)
                await SafeCloseWs(ws, WebSocketCloseStatus.InternalServerError, "UDP connect failed");
            return;
        }

        // Optional: read and ignore app-level "connect" JSON message (text)
        // Your C++ bridge sends it right after handshake.
        await TryDrainOptionalConnectMessage(ws, ct, logger);

        var wsToUdp = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    var msg = await ReceiveWholeWsMessage(ws, ct);
                    logger.LogInformation("WS message: type={Type} bytes={Bytes}", msg.MessageType, msg.Payload.Length);
                    if (msg.MessageType == WebSocketMessageType.Close)
                        break;

                    if (msg.MessageType == WebSocketMessageType.Text)
                    {
                        // Ignore any text control messages
                        continue;
                    }

                    if (msg.MessageType != WebSocketMessageType.Binary || msg.Payload.Length == 0)
                        continue;

                    // Parse one or more datagrams from: [u16_be len][payload]...
                    var data = msg.Payload;
                    var off = 0;

                    while (off + 2 <= data.Length)
                    {
                        var len = (data[off] << 8) | data[off + 1];
                        off += 2;

                        if (len <= 0 || off + len > data.Length)
                        {
                            logger.LogWarning("Invalid framed UDP message. off={Off} len={Len} total={Total}", off, len,
                                data.Length);
                            break;
                        }

                        await udp.SendAsync(data.AsMemory(off, len), ct);
                        off += len;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "WS->UDP pump error. {Message}", e.Message);
            }
        }, ct);

        var udpToWs = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    var pkt = await udp.ReceiveAsync(ct);
                    if (pkt.Buffer.Length <= 0)
                        continue;

                    if (pkt.Buffer.Length > 65535)
                        continue;

                    // Frame: [u16_be len][payload]
                    var framed = new byte[2 + pkt.Buffer.Length];
                    framed[0] = (byte)((pkt.Buffer.Length >> 8) & 0xFF);
                    framed[1] = (byte)(pkt.Buffer.Length & 0xFF);
                    Buffer.BlockCopy(pkt.Buffer, 0, framed, 2, pkt.Buffer.Length);

                    await ws.SendAsync(
                        framed,
                        WebSocketMessageType.Binary,
                        endOfMessage: true,
                        cancellationToken: ct);
                }
            }
            catch (OperationCanceledException)
            {
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

        if (ws.State == WebSocketState.Open)
            await SafeCloseWs(ws, WebSocketCloseStatus.NormalClosure, "Closing");
    }

    private static async Task SafeCloseWs(WebSocket ws, WebSocketCloseStatus status, string reason)
    {
        try
        {
            await ws.CloseAsync(status, reason, CancellationToken.None);
        }
        catch
        {
        }
    }

    private sealed record WsWholeMessage(WebSocketMessageType MessageType, byte[] Payload);

    private static async Task<WsWholeMessage> ReceiveWholeWsMessage(WebSocket ws, CancellationToken ct)
    {
        // Reassembles fragmented WebSocket messages into one payload.
        var buffer = new byte[16 * 1024];
        using var ms = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(buffer, ct);

            if (result.MessageType == WebSocketMessageType.Close)
                return new WsWholeMessage(WebSocketMessageType.Close, Array.Empty<byte>());

            if (result.Count > 0)
                ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return new WsWholeMessage(result.MessageType, ms.ToArray());
    }

    private static async Task TryDrainOptionalConnectMessage(WebSocket ws, CancellationToken ct, ILogger logger)
    {
        // The C++ bridge sends a text JSON immediately after handshake:
        // {"type":"connect","proto":"udp","host":"...","port":...}
        // Your proxy does not need it. We can safely ignore it if present.
        if (ws.State != WebSocketState.Open)
            return;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(250));

        try
        {
            var msg = await ReceiveWholeWsMessage(ws, cts.Token);
            if (msg.MessageType == WebSocketMessageType.Text && msg.Payload.Length > 0)
            {
                var text = System.Text.Encoding.UTF8.GetString(msg.Payload);
                if (text.Contains("\"type\":\"connect\""))
                    logger.LogInformation("Ignored connect control message: {Text}", text);
                else
                    logger.LogInformation("Ignored unexpected text message: {Text}", text);
            }
            else if (msg.MessageType == WebSocketMessageType.Binary)
            {
                // If first message is binary, it is real traffic. Put it back is not possible,
                // so we just do nothing here (and rely on normal loop in HandleUdp).
                // In practice, C++ sends text first, so this should rarely happen.
            }
        }
        catch (OperationCanceledException)
        {
            // No message within 250ms - fine.
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Failed to drain optional connect message. {Message}", e.Message);
        }
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