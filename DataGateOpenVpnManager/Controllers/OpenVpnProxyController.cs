using System.Net.Sockets;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Mvc;

namespace DataGateOpenVpnManager.Controllers;

[ApiController]
[Route("api/proxy")]
public class OpenVpnProxyController(
    ILogger<CertController> logger)
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

        using var ws = await HttpContext.WebSockets.AcceptWebSocketAsync();

        // OpenVPN TCP endpoint (usually localhost or docker service)
        const string targetHost = "127.0.0.1";
        const int targetPort = 1194;

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(targetHost, targetPort);

        await using var tcpStream = tcp.GetStream();

        var ct = HttpContext.RequestAborted;

        var wsToTcp = PumpWebSocketToTcp(ws, tcpStream, ct);
        var tcpToWs = PumpTcpToWebSocket(ws, tcpStream, ct);

        await Task.WhenAny(wsToTcp, tcpToWs);

        try
        {
            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Closing",
                    CancellationToken.None
                );
            }
        }
        catch(Exception e)
        {
            logger.LogError(e, "Error while connecting to websocket. {Message}", e.Message);
        }
    }

    private static async Task PumpWebSocketToTcp(
        WebSocket ws,
        NetworkStream tcp,
        CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];

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

    private static async Task PumpTcpToWebSocket(
        WebSocket ws,
        NetworkStream tcp,
        CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];

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
}