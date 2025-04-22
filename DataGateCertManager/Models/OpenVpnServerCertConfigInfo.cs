namespace DataGateCertManager.Models;

public class OpenVpnServerCertConfigInfo
{
    public int VpnServerId { get; set; }
    public string EasyRsaPath { get; init; } = "/etc/openvpn/easy-rsa";
    public string OvpnFileDir { get; init; } = "/etc/openvpn/clients";
    public string RevokedOvpnFilesDirPath { get; init; } = "/etc/openvpn/clients/revoked/";
    public string PkiPath { get; init; } = "/etc/openvpn/easy-rsa/pki/";
    public string CaCertPath { get; init; } = "/etc/openvpn/easy-rsa/pki/ca.crt";
    public string TlsAuthKey { get; init; } = "/etc/openvpn/easy-rsa/pki/ta.key";
    public string ServerRemoteIp { get; init; } = "0.0.0.0";
    public string CrlPkiPath { get; init; } = "/etc/openvpn/easy-rsa/pki/crl.pem";
    public string CrlOpenvpnPath { get; init; } = "/etc/openvpn/crl.pem";
    public string StatusFilePath { get; init; } = "/var/log/openvpn-status.log";
}