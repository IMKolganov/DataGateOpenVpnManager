namespace DataGateOpenVpnManager.Services.LocalIssuance;

/// <summary>Request rejected by validation (bad CN, IP conflict, ...). Maps to 400/409 on the local channel.</summary>
public sealed class LocalIssuanceValidationException(string message, bool isConflict = false) : Exception(message)
{
    public bool IsConflict { get; } = isConflict;
}
