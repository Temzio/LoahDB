using System.IO.Compression;

namespace LoahDB;

/// <summary>
/// Export and import an entire Loah store directory.
/// </summary>
public static class LoahBackup
{
    public static void Export(LoahStore store, string zipFilePath)
    {
        var sourceDirectory = Path.Combine(store.Options.BasePath, store.Root);
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Store directory was not found: {sourceDirectory}");
        }

        if (File.Exists(zipFilePath))
        {
            File.Delete(zipFilePath);
        }

        ZipFile.CreateFromDirectory(sourceDirectory, zipFilePath, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    public static void Import(LoahStore store, string zipFilePath, bool overwrite = false)
    {
        var targetDirectory = Path.Combine(store.Options.BasePath, store.Root);
        if (Directory.Exists(targetDirectory) && !overwrite)
        {
            throw new InvalidOperationException(
                $"Target store already exists at '{targetDirectory}'. Pass overwrite: true to replace it.");
        }

        if (!Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        ZipFile.ExtractToDirectory(zipFilePath, targetDirectory, overwriteFiles: true);
    }
}
