using LoahDB;

namespace LoahDB.Cli;

public static class LoahCliApp
{
    public static int Run(string[] args, TextWriter? output = null)
    {
        output ??= Console.Out;
        if (args.Length == 0 || args is ["-h"] or ["--help"] or ["help"])
        {
            WriteHelp(output);
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            var options = CliOptions.Parse(args, out var commandArgs);
            return Execute(commandArgs, options, output);
        }
        catch (CliException ex)
        {
            output.WriteLine("error: " + ex.Message);
            return 2;
        }
    }

    private static int Execute(string[] commandArgs, CliOptions options, TextWriter output)
    {
        if (commandArgs.Length == 0)
        {
            throw new CliException("Missing command.");
        }

        var verb = commandArgs[0].ToLowerInvariant();
        using var store = CreateStore(options);
        switch (verb)
        {
            case "info":
                PrintInfo(store, output);
                return 0;
            case "check":
                return PrintIntegrity(store, output);
            case "vacuum":
                store.Vacuum();
                output.WriteLine("Vacuum complete.");
                return 0;
            case "import-legacy":
                store.ImportLegacyV1();
                output.WriteLine("Legacy import complete.");
                return 0;
            case "backup":
                return HandleBackup(commandArgs, store, output);
            default:
                throw new CliException($"Unknown command '{verb}'.");
        }
    }

    private static int HandleBackup(string[] commandArgs, LoahStore store, TextWriter output)
    {
        if (commandArgs.Length < 2)
        {
            throw new CliException("Usage: backup export|import --zip <file>");
        }

        var sub = commandArgs[1].ToLowerInvariant();
        var zip = CliOptions.RequireOption(commandArgs, "--zip");
        switch (sub)
        {
            case "export":
                LoahBackup.Export(store, zip);
                output.WriteLine($"Exported to {zip}");
                return 0;
            case "import":
                var overwrite = commandArgs.Contains("--overwrite", StringComparer.OrdinalIgnoreCase);
                LoahBackup.Import(store, zip, overwrite);
                output.WriteLine($"Imported from {zip}");
                return 0;
            default:
                throw new CliException("Usage: backup export|import --zip <file>");
        }
    }

    private static void PrintInfo(LoahStore store, TextWriter output)
    {
        var info = store.GetInfo();
        output.WriteLine($"Schema version: {info.SchemaVersion}");
        output.WriteLine($"Created: {info.CreatedAtUtc:u}");
        output.WriteLine($"Updated: {info.UpdatedAtUtc:u}");
        output.WriteLine("Collections:");
        foreach (var name in info.Collections.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            output.WriteLine($"  - {name}");
        }
    }

    private static int PrintIntegrity(LoahStore store, TextWriter output)
    {
        var report = store.CheckIntegrity();
        output.WriteLine($"Pages checked: {report.PagesChecked}");
        output.WriteLine($"Documents checked: {report.DocumentsChecked}");
        if (report.IsValid)
        {
            output.WriteLine("OK");
            return 0;
        }

        output.WriteLine("FAILED");
        foreach (var error in report.Errors)
        {
            output.WriteLine($"  {error}");
        }

        return 3;
    }

    private static LoahStore CreateStore(CliOptions options) =>
        new(options.Root, new LoahOptions
        {
            BasePath = options.BasePath,
            EncryptionKey = options.EncryptionKey,
            StorageFormat = LoahStorageFormat.PageFile,
        });

    private static void WriteHelp(TextWriter output)
    {
        output.WriteLine(
            """
            loah — LoahDB command-line tool

            Usage:
              loah [--path <dir>] [--root <name>] [--key <passphrase>] <command>

            Commands:
              info                 Show store metadata
              check                Run CheckIntegrity
              vacuum               Compact page file
              import-legacy        Import _collections/*.loah into page store
              backup export --zip <file>
              backup import --zip <file> [--overwrite]
            """);
    }

    private sealed class CliOptions
    {
        public string BasePath { get; init; } = Environment.CurrentDirectory;
        public string Root { get; init; } = "default";
        public string? EncryptionKey { get; init; }

        public static CliOptions Parse(string[] args, out string[] commandArgs)
        {
            var path = Environment.CurrentDirectory;
            var root = "default";
            string? key = null;
            var remaining = new List<string>();
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--path":
                        path = RequireValue(args, ref i);
                        break;
                    case "--root":
                        root = RequireValue(args, ref i);
                        break;
                    case "--key":
                        key = RequireValue(args, ref i);
                        break;
                    default:
                        remaining.Add(args[i]);
                        break;
                }
            }

            commandArgs = remaining.ToArray();
            return new CliOptions { BasePath = path, Root = root, EncryptionKey = key };
        }

        public static string RequireOption(string[] args, string name)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            throw new CliException($"Missing required option {name}.");
        }

        private static string RequireValue(string[] args, ref int index)
        {
            if (index + 1 >= args.Length)
            {
                throw new CliException($"Missing value for {args[index]}.");
            }

            index++;
            return args[index];
        }
    }

    private sealed class CliException : Exception
    {
        public CliException(string message) : base(message)
        {
        }
    }
}
