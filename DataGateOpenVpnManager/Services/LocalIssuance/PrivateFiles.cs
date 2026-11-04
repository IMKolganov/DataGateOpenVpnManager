namespace DataGateOpenVpnManager.Services.LocalIssuance;

/// <summary>Owner-only (0700/0600) helpers for the local issuance directory.</summary>
internal static class PrivateFiles
{
    private const UnixFileMode OwnerDir = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode OwnerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static void EnsurePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        Directory.CreateDirectory(path, OwnerDir);
        // entrypoint.sh runs "chmod -R a+rX $DATA_DIR" before the app starts — tighten again.
        File.SetUnixFileMode(path, OwnerDir);
    }

    public static void RestrictToOwner(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, OwnerFile);
    }

    /// <summary>Write via temp file + flush + rename so a crash never leaves a half-written file.</summary>
    public static void WriteAllTextAtomic(string path, string content)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            RestrictToOwner(temp);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
