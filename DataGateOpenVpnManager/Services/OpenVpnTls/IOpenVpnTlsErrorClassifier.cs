namespace DataGateOpenVpnManager.Services.OpenVpnTls;

public interface IOpenVpnTlsErrorClassifier
{
    bool IsTlsCryptLine(string line);
    OpenVpnTlsErrorContext Classify(string line);
}
