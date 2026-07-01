using DataGateOpenVpnManager.Services.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.PiHole.Dto;

namespace DataGateOpenVpnManager.Services.PiHole;

public interface IPiHoleClientIdentityResolver
{
    IReadOnlyList<DnsQueryEventDto> Enrich(
        IEnumerable<PiHoleQueryRecord> records,
        OpenVpnManagementStatusSnapshot? managementSnapshot);
}

public sealed class PiHoleClientIdentityResolver : IPiHoleClientIdentityResolver
{
    public IReadOnlyList<DnsQueryEventDto> Enrich(
        IEnumerable<PiHoleQueryRecord> records,
        OpenVpnManagementStatusSnapshot? managementSnapshot)
    {
        var clients = managementSnapshot?.Clients ?? Array.Empty<OpenVpnManagementClientEntry>();
        return records
            .Select(record =>
            {
                var match = OpenVpnManagementStatusParser.FindByVirtualAddress(clients, record.ClientIp);
                return new DnsQueryEventDto
                {
                    PiHoleQueryId = record.PiHoleQueryId,
                    ClientIp = record.ClientIp,
                    CommonName = match?.CommonName,
                    Domain = record.Domain,
                    QueryType = record.QueryType,
                    Status = record.Status,
                    QueriedAtUtc = record.QueriedAtUtc
                };
            })
            .ToList();
    }
}
