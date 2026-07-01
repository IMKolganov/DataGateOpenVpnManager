namespace DataGateOpenVpnManager.Services.PiHole;

public sealed record PiHoleQueryRecord(
    long PiHoleQueryId,
    string ClientIp,
    string Domain,
    string? QueryType,
    string Status,
    DateTimeOffset QueriedAtUtc);
