namespace DataGateOpenVpnManager.Tests.TestSupport;

/// <summary>Locates repo files (scripts/, test fixtures) from the test output directory.</summary>
internal static class RepoPaths
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DataGateOpenVpnManager.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("DataGateOpenVpnManager.sln not found above test output.");
        }
    }

    public static string Combine(params string[] parts) => Path.Combine([Root, .. parts]);
}

/// <summary>Short temp dir (Unix socket paths are limited to 107 bytes).</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine("/tmp", "dg-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
