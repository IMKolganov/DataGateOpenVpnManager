using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.PiHole;

namespace DataGateOpenVpnManager.Tests.Services.PiHole;

public class PiHoleCredentialDiagnosticsTests
{
    [Fact]
    public void CredentialSource_IsNone_WhenNodeHasNoPassword()
    {
        var sut = PiHoleRuntimeOptionsStoreTestHelper.Create(new PiHoleOptions());
        Assert.Equal("None", sut.CredentialSource);
    }

    [Fact]
    public void CredentialSource_IsBootstrap_WhenPasswordComesFromNodeDefaults()
    {
        var sut = PiHoleRuntimeOptionsStoreTestHelper.Create(new PiHoleOptions { AppPassword = "synthetic-credential" });
        Assert.Equal("Bootstrap", sut.CredentialSource);
    }

    [Fact]
    public void CredentialSource_IsDashboard_AfterRuntimeApply()
    {
        var sut = PiHoleRuntimeOptionsStoreTestHelper.Create(
            new PiHoleOptions { AppPassword = "synthetic-credential" },
            dataDir: Path.Combine(Path.GetTempPath(), "pihole-source-" + Guid.NewGuid().ToString("N")));

        sut.Apply(new PiHoleOptions { AppPassword = "applied-credential" });

        Assert.Equal("Dashboard", sut.CredentialSource);
    }

    [Fact]
    public void CredentialSource_IsEnv_WhenLegacyVariableOverridesDashboard()
    {
        var sut = PiHoleRuntimeOptionsStoreTestHelper.Create(
            dataDir: Path.Combine(Path.GetTempPath(), "pihole-source-" + Guid.NewGuid().ToString("N")),
            env: new Dictionary<string, string?> { ["PIHOLE_APP_PASSWORD"] = "synthetic-credential" });

        sut.Apply(new PiHoleOptions { AppPassword = "applied-credential" });

        Assert.Equal("Env", sut.CredentialSource);
    }

    [Theory]
    [InlineData("", "synthetic-credential", "https://backend.example.com/", PiHoleAnnounceState.NoCredentials)]
    [InlineData("http://10.51.40.1:8080", "", "https://backend.example.com/", PiHoleAnnounceState.NoCredentials)]
    [InlineData("http://10.51.40.1:8080", "synthetic-credential", null, PiHoleAnnounceState.BackendNotConfigured)]
    [InlineData("http://10.51.40.1:8080", "synthetic-credential", "http://backend.example.com/", PiHoleAnnounceState.OmittedInsecureBackend)]
    [InlineData("http://10.51.40.1:8080", "synthetic-credential", "http://127.0.0.1:5581/", PiHoleAnnounceState.Included)]
    [InlineData("http://10.51.40.1:8080", "synthetic-credential", "https://backend.example.com/", PiHoleAnnounceState.Included)]
    public void AnnounceState_ReflectsWhatTheAnnounceWouldDo(string baseUrl, string password, string? backend, string expected)
    {
        var options = new PiHoleOptions { BaseUrl = baseUrl, AppPassword = password };
        Assert.Equal(expected, PiHoleAnnounceState.Resolve(options, backend is null ? null : new Uri(backend)));
    }

    [Fact]
    public void Factory_PassesDiagnosticsThrough_WithoutThePassword()
    {
        var response = PiHoleDiagnosticsFactory.Create(
            new PiHoleOptions { AppPassword = "synthetic-credential" },
            new PiHoleCollectorStatusSnapshot(),
            null,
            (Authenticated: false, SampleQueryCount: 0, Error: null),
            credentialSource: "Bootstrap",
            announceState: PiHoleAnnounceState.Included);

        Assert.Equal("Bootstrap", response.CredentialSource);
        Assert.Equal(PiHoleAnnounceState.Included, response.AnnounceState);
        Assert.DoesNotContain("synthetic-credential", System.Text.Json.JsonSerializer.Serialize(response));
    }
}
