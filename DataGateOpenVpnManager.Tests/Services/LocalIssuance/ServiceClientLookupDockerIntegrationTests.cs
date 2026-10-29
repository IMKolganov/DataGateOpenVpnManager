using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using DataGateOpenVpnManager.Helpers;
using DataGateOpenVpnManager.Tests.TestSupport;
using Xunit.Abstractions;

namespace DataGateOpenVpnManager.Tests.Services.LocalIssuance;

/// <summary>Real entrypoint, Kestrel, OpenVPN, EasyRSA and VPN clients on a disposable internal Docker network.</summary>
public sealed class ServiceClientLookupDockerIntegrationTests(ITestOutputHelper output)
{
    private const string ImageVariable = "DATAGATE_LOOKUP_TEST_IMAGE";
    private const int LookupPort = 15112;
    private const string LookupPath = "/api/local/service-clients/by-virtual-ip/";

    [LookupDockerFact]
    public async Task ConfiguredNode_OnlyServiceClientsAreFound_DisconnectAndCrlHideThem()
    {
        var image = Environment.GetEnvironmentVariable(ImageVariable)!;
        var prefix = "dgm-lookup-it-" + Guid.NewGuid().ToString("N")[..10];
        var network = prefix + "-net";
        var backend = prefix + "-auth";
        var hub = prefix + "-hub";
        var disabled = prefix + "-default";
        var serviceClient = prefix + "-service";
        var userClient = prefix + "-user";
        var containers = new List<string>();
        using var dir = new TempDir();
        await DockerAsync("network", "create", "--internal", network);
        try
        {
            // The real application needs an auth-key response at startup. This isolated stub stores nothing.
            var nginx = dir.Combine("nginx.conf");
            await File.WriteAllTextAsync(nginx, """
                events {}
                http { server { listen 8080; location / {
                    default_type application/json;
                    return 200 '{"success":true,"data":"integration-only"}';
                } } }
                """);
            containers.Add(backend);
            await DockerAsync("run", "-d", "--name", backend, "--network", network,
                "--mount", $"type=bind,src={nginx},dst=/tmp/lookup-nginx.conf,readonly",
                "--entrypoint", "nginx", image, "-c", "/tmp/lookup-nginx.conf", "-g", "daemon off;");

            async Task StartNode(string name, bool enabled)
            {
                containers.Add(name);
                var args = new List<string> { "run", "-d", "--name", name, "--network", network,
                    "--cap-add", "NET_ADMIN", "--device", "/dev/net/tun:/dev/net/tun" };
                foreach (var value in new[] { "LOCAL_ISSUANCE_ENABLED=true", $"SERVICE_CLIENT_LOOKUP_PORT={LookupPort}",
                    "DATA_DIR=/mnt", "EASY_RSA_PATH=/mnt/easy-rsa", "PUBLIC_IP=192.0.2.1",
                    "OpenVpnManagement__Host=127.0.0.1", "OpenVpnManagement__Port=5092",
                    $"Backend__BaseUrl=http://{backend}:8080/", "VPN_SUBNET=10.89.94.0",
                    "DOTNET_USE_POLLING_FILE_WATCHER=1", "OpenVpnProxy__TlsLogEnrichmentEnabled=false" })
                    args.AddRange(["-e", value]);
                if (enabled)
                    args.AddRange(["-e", "SERVICE_CLIENT_LOOKUP=true"]);
                args.Add(image);
                await DockerAsync([.. args]);
                await WaitUntilAsync(async () =>
                    (await DockerAsync(true, "exec", name, "test", "-S", "/mnt/local-issuance/admin.sock")).ExitCode == 0);
            }

            await StartNode(disabled, enabled: false);
            Assert.Equal(404, (await RequestAsync(disabled, 5010, "10.89.94.2")).Status);
            var noListener = await DockerAsync(true, "exec", disabled, "curl", "-sS", "--max-time", "2",
                $"http://127.0.0.1:{LookupPort}{LookupPath}10.89.94.2");
            Assert.NotEqual(0, noListener.ExitCode);

            await StartNode(hub, enabled: true);
            var version = await DockerAsync("exec", hub, "openvpn", "--version");
            output.WriteLine(version.StdOut.Split('\n')[0]);

            async Task<string> CreateProfile(string cn, bool service)
            {
                var args = new List<string> { "exec", hub, "datagate-profile", "create", cn, "--remote", hub };
                if (service)
                    args.Add("--service");
                var issued = await DockerAsync([.. args]);
                var path = dir.Combine(cn + ".ovpn");
                await File.WriteAllTextAsync(path, issued.StdOut);
                if (OperatingSystem.IsLinux())
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                return path;
            }

            var serviceProfile = await CreateProfile("integration-service", service: true);
            var userProfile = await CreateProfile("integration-user", service: false);

            async Task StartClient(string name, string profile)
            {
                containers.Add(name);
                await DockerAsync("run", "-d", "--name", name, "--network", network,
                    "--cap-add", "NET_ADMIN", "--device", "/dev/net/tun:/dev/net/tun",
                    "--mount", $"type=bind,src={profile},dst=/client.ovpn,readonly",
                    "--entrypoint", "openvpn", image, "--config", "/client.ovpn", "--disable-dco",
                    "--explicit-exit-notify", "1", "--log", "/tmp/lookup-client.log", "--verb", "3");
                await ClientReadyAsync(name);
            }

            await StartClient(serviceClient, serviceProfile);
            await StartClient(userClient, userProfile);
            var serviceIp = await ClientIpAsync(serviceClient);
            var userIp = await ClientIpAsync(userClient);
            var found = await RequestAsync(hub, LookupPort, serviceIp);
            if (found.Status != 200)
            {
                var log = await DockerAsync("logs", hub);
                foreach (var line in log.StdOut.Split('\n').Where(l => l.Contains("CLIENT_LIST") || l.Contains("lookup unavailable")
                    || l.Contains("ManagementStatusCache") || l.Contains("Attempting to connect")))
                    output.WriteLine(line);
                output.WriteLine((await DockerAsync("exec", hub, "cat", "/mnt/local-issuance/profiles.json")).StdOut);
                output.WriteLine((await DockerAsync("exec", hub, "cat", "/mnt/easy-rsa/pki/index.txt")).StdOut);
            }
            Assert.Equal(200, found.Status);
            using (var json = JsonDocument.Parse(found.Body))
            {
                var body = json.RootElement;
                Assert.Equal("integration-service", body.GetProperty("commonName").GetString());
                Assert.Equal(serviceIp, body.GetProperty("virtualAddress").GetString());
                Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("realAddress").GetString()));
                Assert.InRange(body.GetProperty("connectedSince").GetDateTimeOffset(), DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow);
                var cert = await DockerAsync("exec", hub, "openssl", "x509", "-in",
                    "/mnt/easy-rsa/pki/issued/integration-service.crt", "-serial", "-noout");
                Assert.True(CertificateSerial.AreEqual(cert.StdOut.Trim().Split('=')[1], body.GetProperty("serial").GetString()));
            }
            Assert.Equal(404, (await RequestAsync(hub, LookupPort, userIp)).Status);
            Assert.Equal(404, (await RequestAsync(hub, 5010, serviceIp)).Status);

            // A read-only lookup must not run EasyRSA initialization or regenerate a missing TLS key.
            await DockerAsync("exec", hub, "mv", "/mnt/easy-rsa/pki/ta.key", "/mnt/easy-rsa/pki/ta.key.saved");
            try
            {
                Assert.Equal(200, (await RequestAsync(hub, LookupPort, serviceIp)).Status);
                Assert.NotEqual(0, (await DockerAsync(true, "exec", hub, "test", "-e", "/mnt/easy-rsa/pki/ta.key")).ExitCode);
            }
            finally
            {
                await DockerAsync("exec", hub, "mv", "/mnt/easy-rsa/pki/ta.key.saved", "/mnt/easy-rsa/pki/ta.key");
            }

            // Same network namespace as the gateway: loopback works without any Authorization header.
            var shared = await DockerAsync("run", "--rm", "--network", "container:" + hub,
                "--entrypoint", "curl", image, "-sS", "-o", "/dev/null", "-w", "%{http_code}",
                $"http://127.0.0.1:{LookupPort}{LookupPath}{serviceIp}");
            Assert.Equal("200", shared.StdOut);
            // A separate container cannot connect to the private listener, and the public API hides the route.
            var remotePrivate = await DockerAsync(true, "exec", backend, "curl", "-sS", "--connect-timeout", "1",
                $"http://{hub}:{LookupPort}{LookupPath}{serviceIp}");
            Assert.NotEqual(0, remotePrivate.ExitCode);
            var remotePublic = await DockerAsync("exec", backend, "curl", "-sS", "-o", "/dev/null", "-w", "%{http_code}",
                $"http://{hub}:5010{LookupPath}{serviceIp}");
            Assert.Equal("404", remotePublic.StdOut);

            var samples = new List<double>();
            for (var i = 0; i < 30; i++)
            {
                var sample = await RequestAsync(hub, LookupPort, serviceIp);
                Assert.Equal(200, sample.Status);
                samples.Add(sample.Milliseconds);
            }
            samples.Sort();
            var measured = new { samples = samples.Count, connectedClients = 2,
                minMs = samples[0], medianMs = samples[samples.Count / 2],
                p95Ms = samples[(int)Math.Ceiling(samples.Count * .95) - 1], maxMs = samples[^1] };
            output.WriteLine("HTTP lookup latency (curl time_total inside hub): " + JsonSerializer.Serialize(measured));
            var disconnectStarted = Stopwatch.StartNew();
            await DockerAsync("stop", "--time", "5", serviceClient);
            // The server must first process OpenVPN's UDP exit notification; process exit alone is not peer removal.
            await WaitUntilAsync(async () =>
            {
                var log = await DockerAsync("exec", hub, "cat", "/mnt/openvpn.log");
                return log.StdOut.Split('\n').Any(l => l.Contains("integration-service/") && l.Contains("client-instance exiting"));
            });
            var disconnected = await RequestAsync(hub, LookupPort, serviceIp);
            Assert.Equal(404, disconnected.Status);
            var disconnectElapsedMs = disconnectStarted.Elapsed.TotalMilliseconds;
            output.WriteLine($"First lookup after graceful disconnect: 404 in {disconnected.Milliseconds:F3} ms");
            await DockerAsync("start", serviceClient);
            await ClientReadyAsync(serviceClient);
            serviceIp = await ClientIpAsync(serviceClient);
            Assert.Equal(200, (await RequestAsync(hub, LookupPort, serviceIp)).Status);

            var beforeIndex = (await DockerAsync("exec", hub, "cat", "/mnt/easy-rsa/pki/index.txt")).StdOut;
            var beforeRegistry = (await DockerAsync("exec", hub, "cat", "/mnt/local-issuance/profiles.json")).StdOut;
            await DockerAsync("exec", hub, "datagate-profile", "revoke", "integration-service");
            var revoked = await RequestAsync(hub, LookupPort, serviceIp);
            Assert.Equal(404, revoked.Status);
            output.WriteLine($"First lookup after revoke: 404 in {revoked.Milliseconds:F3} ms");
            // Restore only index/registry in this disposable fixture. The real generated CRL stays revoked.
            await DockerInputAsync(beforeIndex, "exec", "-i", hub, "sh", "-c", "cat > /mnt/easy-rsa/pki/index.txt");
            await DockerInputAsync(beforeRegistry, "exec", "-i", hub, "sh", "-c", "cat > /mnt/local-issuance/profiles.json");
            var crlOnly = await RequestAsync(hub, LookupPort, serviceIp);
            Assert.Equal(404, crlOnly.Status);
            var diagnostics = await DockerAsync("exec", hub, "curl", "-sS", "http://127.0.0.1:5010/api/diagnostics/proxy-sessions");
            using var diagnosticJson = JsonDocument.Parse(diagnostics.StdOut);
            Assert.Equal(2, diagnosticJson.RootElement.GetProperty("data").GetProperty("managementClientCount").GetInt32());
            output.WriteLine($"CRL-only revocation with both VPN sessions still connected: 404 in {crlOnly.Milliseconds:F3} ms");
            if (Environment.GetEnvironmentVariable("DATAGATE_LOOKUP_TEST_REPORT") is { Length: > 0 } report)
                await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { latency = measured,
                    coldLookupMs = found.Milliseconds, disconnectElapsedMs, disconnectLookupMs = disconnected.Milliseconds,
                    revokeLookupMs = revoked.Milliseconds, crlOnlyLookupMs = crlOnly.Milliseconds }));
        }
        finally
        {
            foreach (var container in containers.AsEnumerable().Reverse())
                await DockerAsync(true, "rm", "-f", "-v", container);
            await DockerAsync(true, "network", "rm", network);
        }
    }

    private static async Task ClientReadyAsync(string name) => await WaitUntilAsync(async () =>
        (await DockerAsync(true, "exec", name, "cat", "/tmp/lookup-client.log")).StdOut.Contains("Initialization Sequence Completed"));

    private static async Task<string> ClientIpAsync(string name)
    {
        var result = await DockerAsync("exec", name, "ip", "-j", "-4", "address", "show", "dev", "tun0");
        using var json = JsonDocument.Parse(result.StdOut);
        return json.RootElement[0].GetProperty("addr_info")[0].GetProperty("local").GetString()!;
    }

    private static async Task<(int Status, string Body, double Milliseconds)> RequestAsync(string node, int port, string ip)
    {
        var result = await DockerAsync("exec", node, "curl", "-sS", "--max-time", "5",
            "-w", "\n%{http_code}\n%{time_total}", $"http://127.0.0.1:{port}{LookupPath}{ip}");
        var lines = result.StdOut.TrimEnd().Split('\n');
        return (int.Parse(lines[^2], CultureInfo.InvariantCulture), string.Join('\n', lines[..^2]),
            double.Parse(lines[^1], CultureInfo.InvariantCulture) * 1000);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> ready)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(35))
        {
            if (await ready())
                return;
            await Task.Delay(150);
        }
        Assert.Fail("Isolated OpenVPN fixture did not become ready within 35 seconds.");
    }

    private static Task<(int ExitCode, string StdOut)> DockerAsync(params string[] args) => DockerInputAsync(null, false, args);
    private static Task<(int ExitCode, string StdOut)> DockerAsync(bool allowFailure, params string[] args) =>
        DockerInputAsync(null, allowFailure, args);
    private static Task<(int ExitCode, string StdOut)> DockerInputAsync(string input, params string[] args) => DockerInputAsync(input, false, args);

    private static async Task<(int ExitCode, string StdOut)> DockerInputAsync(string? input, bool allowFailure, params string[] args)
    {
        var psi = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await process.WaitForExitAsync(timeout.Token);
        var error = await stderr;
        if (!allowFailure && process.ExitCode != 0)
            throw new InvalidOperationException($"docker {args[0]} failed ({process.ExitCode}): {error}");
        return (process.ExitCode, await stdout);
    }

    private sealed class LookupDockerFactAttribute : FactAttribute
    {
        public LookupDockerFactAttribute()
        {
            if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ImageVariable)))
                Skip = $"Set {ImageVariable} to an image built from the current checkout; Docker and /dev/net/tun are required.";
            Timeout = 180_000;
        }
    }
}
