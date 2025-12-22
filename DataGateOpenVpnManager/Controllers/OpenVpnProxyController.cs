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
    [HttpGet]
    public async Task Get()
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

        using var ws = await HttpContext.WebSockets.AcceptWebSocketAsync();

        using var tcp = new TcpClient { NoDelay = true };
        try
        {
            await tcp.ConnectAsync(targetHost, vpnPort);
        }
        catch (Exception e)
        {
            logger.LogError(e, "TCP connect failed. {Host}:{Port}. {Message}", targetHost, vpnPort, e.Message);

            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.InternalServerError, "TCP connect failed", CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                logger.LogTrace("Something went wrong. When ... {Message}", ex.Message);
            }

            return;
        }

        await using var tcpStream = tcp.GetStream();

        var ct = HttpContext.RequestAborted;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var wsToTcp = PumpWebSocketToTcp(ws, tcpStream, linkedCts.Token, logger);
        var tcpToWs = PumpTcpToWebSocket(ws, tcpStream, linkedCts.Token, logger);

        await Task.WhenAny(wsToTcp, tcpToWs);
        await linkedCts.CancelAsync();

        try
        {
            await Task.WhenAll(wsToTcp, tcpToWs);
        }
        catch (Exception ex)
        {
            logger.LogTrace("Something went wrong. {Message}", ex.Message);
        }

        try
        {
            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "WebSocket close error. {Message}", e.Message);
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

                await tcp.FlushAsync(ct);
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
