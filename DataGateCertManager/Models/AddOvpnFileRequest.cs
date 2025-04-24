using System.ComponentModel;

namespace DataGateCertManager.Models;

public class AddOvpnFileRequest
{
    [DefaultValue("")]
    public string CommonName { get; set; } = string.Empty;

    [DefaultValue(365)]
    public int OvpnFileExpireDays { get; set; } = 365;

    [DefaultValue("localhost")]
    public string ServerIp { get; set; } = "localhost";

    [DefaultValue(1194)]
    public int ServerPort { get; set; } = 1194;

    [DefaultValue(
        "client\ndev tun\nproto udp\nremote {{server_ip}} {{server_port}}\nresolv-retry infinite\nnobind" +
        "\nremote-cert-tls server\ntls-version-min 1.2\ncipher AES-256-CBC\nauth SHA256\nauth-nocache\nverb 3" +
        "\n<ca>\n{{ca_cert}}\n</ca>\n<cert>\n{{client_cert}}\n</cert>\n<key>\n{{client_key}}\n</key>\n<tls-crypt>" +
        "\n{{tls_auth_key}}\n</tls-crypt>"
    )]
    public string ConfigTemplate { get; set; } = "client\ndev tun\nproto udp\nremote {{server_ip}} {{server_port}}\nresolv-retry infinite\nnobind" +
                                                 "\nremote-cert-tls server\ntls-version-min 1.2\ncipher AES-256-CBC\nauth SHA256\nauth-nocache\nverb 3" +
                                                 "\n<ca>\n{{ca_cert}}\n</ca>\n<cert>\n{{client_cert}}\n</cert>\n<key>\n{{client_key}}\n</key>\n<tls-crypt>" +
                                                 "\n{{tls_auth_key}}\n</tls-crypt>";

    [DefaultValue("openVpnClient")]
    public string IssuedTo { get; set; } = "openVpnClient";
}