namespace DataGateOpenVpnManager.Services.Proxy;

public sealed record ProxyByteComparison(
    long ProxyClientToServer,
    long ProxyServerToClient,
    long ManagementBytesReceived,
    long ManagementBytesSent,
    long DeltaClientToServer,
    long DeltaServerToClient)
{
    public static ProxyByteComparison Create(
        long proxyClientToServer,
        long proxyServerToClient,
        long managementBytesReceived,
        long managementBytesSent)
    {
        return new ProxyByteComparison(
            proxyClientToServer,
            proxyServerToClient,
            managementBytesReceived,
            managementBytesSent,
            proxyClientToServer - managementBytesReceived,
            proxyServerToClient - managementBytesSent);
    }

    public bool HasMaterialDelta(long warnDeltaBytes) =>
        Math.Abs(DeltaClientToServer) >= warnDeltaBytes
        || Math.Abs(DeltaServerToClient) >= warnDeltaBytes;
}
