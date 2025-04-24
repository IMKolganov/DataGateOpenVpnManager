namespace DataGateCertManager.Models;

public class AddOvpnFileRequest
{
    public string CommonName { get; set; } = string.Empty;
    public int OvpnFileExpireDays { get; set; } = 365;
    public string ServerIp { get; set; } = "localhost";
    public int ServerPort { get; set; } = 1194 ;
    public string ConfigTemplate { get; set; } = 
        "client\ndev tun\nproto udp\nremote {{server_ip}} {{server_port}}\nresolv-retry infinite\nnobind" +
        "\nremote-cert-tls server\ntls-version-min 1.2\ncipher AES-256-CBC\nauth SHA256\nauth-nocache\nverb 3" +
        "\n<ca>\n{{ca_cert}}\n</ca>\n<cert>\n{{client_cert}}\n</cert>\n<key>\n{{client_key}}\n</key>\n<tls-crypt>" +
        "\n{{tls_auth_key}}\n</tls-crypt>";
}