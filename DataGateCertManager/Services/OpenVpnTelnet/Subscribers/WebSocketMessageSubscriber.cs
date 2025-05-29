using System.Net.WebSockets;
using System.Text;

namespace DataGateCertManager.Services.OpenVpnTelnet.Subscribers;

public class WebSocketMessageSubscriber(WebSocket webSocket) : IMessageSubscriber
{
    public async Task OnMessageReceived(string message, CancellationToken cancellationToken)
    {
        if (webSocket.State == WebSocketState.Open)
        {
            var messageBytes = Encoding.UTF8.GetBytes(message);
            await webSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, 
                true, cancellationToken);
        }
        else
        {
            throw new WebSocketException("The websocket is not open.");
        }
    }
}
