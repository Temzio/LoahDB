using System.IO.Compression;

namespace LoahDB;

/// <summary>
/// Export and import an entire Loah store directory.
/// </summary>
public static class LoahBackup
{
    public static void Export(LoahStore store, string zipFilePath)
    {
        store.CheckpointForBackup();

        var sourceDirectory = Path.Combine(store.Options.BasePath, store.Root);
        if (!Directory.Exists(sourceDirectory))
        {
            Directory.CreateDirectory(sourceDirectory);
        }

        if (File.Exists(zipFilePath))
        {
            File.Delete(zipFilePath);
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "loah-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            CopyDirectory(sourceDirectory, Path.Combine(tempDir, store.Root));
            var pageFile = Path.Combine(store.Options.BasePath, store.Root + ".loahdb");
            if (File.Exists(pageFile))
            {
                var dest = Path.Combine(tempDir, store.Root + ".loahdb");
                File.Copy(pageFile, dest, overwrite: true);
            }

            var walFile = pageFile + "-wal";
            if (File.Exists(walFile))
            {
                File.Copy(walFile, Path.Combine(tempDir, store.Root + ".loahdb-wal"), overwrite: true);
            }

            ZipFile.CreateFromDirectory(tempDir, zipFilePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    public static void Import(LoahStore store, string zipFilePath, bool overwrite = false)
    {
        var targetDirectory = Path.Combine(store.Options.BasePath, store.Root);
        if (Directory.Exists(targetDirectory) && !overwrite)
        {
            throw new InvalidOperationException(
                $"Target store already exists at '{targetDirectory}'. Pass overwrite: true to replace it.");
        }

        var extractRoot = Path.Combine(Path.GetTempPath(), "loah-import-" + Guid.NewGuid().ToString("N"));
        ZipFile.ExtractToDirectory(zipFilePath, extractRoot, overwriteFiles: true);
        try
        {
            var extractedStore = ResolveStoreDirectory(extractRoot, store.Root);
            CopyDirectory(extractedStore, targetDirectory);
            var pageCandidate = Path.Combine(extractRoot, store.Root + ".loahdb");
            if (File.Exists(pageCandidate))
            {
                File.Copy(pageCandidate, Path.Combine(store.Options.BasePath, store.Root + ".loahdb"), overwrite);
            }

            var walCandidate = Path.Combine(extractRoot, store.Root + ".loahdb-wal");
            if (File.Exists(walCandidate))
            {
                File.Copy(walCandidate, Path.Combine(store.Options.BasePath, store.Root + ".loahdb-wal"), overwrite);
            }
        }
        finally
        {
            if (Directory.Exists(extractRoot))
            {
                Directory.Delete(extractRoot, recursive: true);
            }
        }
    }

    private static string ResolveStoreDirectory(string extractRoot, string storeRoot)
    {
        var named = Path.Combine(extractRoot, storeRoot);
        if (File.Exists(Path.Combine(named, "_meta")) || Directory.Exists(Path.Combine(named, "_collections")))
        {
            return named;
        }

        if (File.Exists(Path.Combine(extractRoot, "_meta")) || Directory.Exists(Path.Combine(extractRoot, "_collections")))
        {
            return extractRoot;
        }

        foreach (var dir in Directory.GetDirectories(extractRoot))
        {
            if (File.Exists(Path.Combine(dir, "_meta")) || Directory.Exists(Path.Combine(dir, "_collections")))
            {
                return dir;
            }
        }

        return extractRoot;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }
}
