using System.Diagnostics;
using DataGateOpenVpnManager.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DataGateOpenVpnManager.Tests.Services.LocalIssuance;

/// <summary>Runs the real <c>scripts/datagate-profile.sh</c> (bash + curl) against a Kestrel admin Unix socket.</summary>
public class DatagateProfileScriptTests
{
    private const string Ovpn = "client\nremote 203.0.113.10 1194\n";

    private static async Task<WebApplication> StartSocketAsync(string socketPath)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.ListenUnixSocket(socketPath));
        var app = builder.Build();
        app.MapGet("/api/local-admin/profiles/db1/ovpn", () => Results.Text(Ovpn));
        app.MapGet("/api/local-admin/profiles/gone/ovpn", () =>
            Results.Json(new { success = false, message = "'gone' is revoked" }, statusCode: 400));
        await app.StartAsync();
        return app;
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(string socketPath, params string[] args)
    {
        var psi = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(RepoPaths.Combine("scripts", "datagate-profile.sh"));
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.Environment["LOCAL_ADMIN_SOCKET"] = socketPath;
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout, await stderr);
    }

    private static bool CanRun() =>
        OperatingSystem.IsLinux() && File.Exists("/usr/bin/curl") && File.Exists("/bin/bash");

    [Fact]
    public async Task Create_StaticIpIsAnUnknownOption()
    {
        if (!CanRun())
            return;
        var (code, stdout, stderr) = await RunAsync("/tmp/no-admin.sock", "create", "db1", "--static-ip", "10.50.28.201");
        Assert.NotEqual(0, code);
        Assert.Empty(stdout);
        Assert.Contains("unknown option: --static-ip", stderr);
    }

    [Fact]
    public async Task Get_WhenNodeRefuses_FailsWithoutWritingAFile()
    {
        if (!CanRun())
            return;

        using var dir = new TempDir();
        var socket = dir.Combine("admin.sock");
        await using var app = await StartSocketAsync(socket);
        var output = dir.Combine("gone.ovpn");

        var (exitCode, stdout, stderr) = await RunAsync(socket, "get", "gone", "-o", output);

        Assert.Equal(1, exitCode);
        Assert.Empty(stdout);
        Assert.Contains("HTTP 400", stderr);
        Assert.DoesNotContain("wrote", stderr);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task Get_WritesPrivateFile()
    {
        if (!CanRun())
            return;

        using var dir = new TempDir();
        var socket = dir.Combine("admin.sock");
        await using var app = await StartSocketAsync(socket);
        var output = dir.Combine("db1.ovpn");

        var (exitCode, _, _) = await RunAsync(socket, "get", "db1", "-o", output);

        Assert.Equal(0, exitCode);
        Assert.Equal(Ovpn, await File.ReadAllTextAsync(output));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(output));
    }
}
