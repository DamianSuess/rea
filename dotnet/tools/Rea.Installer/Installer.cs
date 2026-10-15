using System.Diagnostics;
using System.Xml.Linq;
using Rea.Automation;

namespace Rea.Installer;

public static class Installer
{
    public static async Task<int> RunAsync(string[] args, TextReader input, TextWriter output, TextWriter error)
    {
        string? staging = null;
        try
        {
            var dryRun = false; var yes = false; string? package = null; string? destination = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < args.Length; i++)
            {
                var option = args[i];
                if (!seen.Add(option)) throw new ArgumentException("Duplicate option: " + option);
                switch (option)
                {
                    case "--help" or "-h":
                        await output.WriteLineAsync("REA C# installer (.NET 10 SDK required)\nUsage: install.sh [--package FILE.nupkg] [--tool-path DIRECTORY] [--dry-run] [--yes] [--no-setup|--no-prompt]\nWithout --package, builds and packs this checkout. Local installation only; no PATH edits or provider setup. --no-prompt requires --yes to install.");
                        return 0;
                    case "--dry-run": dryRun = true; break;
                    case "--yes": yes = true; break;
                    case "--no-setup" or "--no-prompt": break;
                    case "--package" or "--tool-path":
                        if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Missing value for " + option);
                        if (option == "--package") package = Path.GetFullPath(args[i]); else destination = Path.GetFullPath(args[i]);
                        break;
                    default: throw new ArgumentException("Unknown option: " + option + ". Use --help; registry --version installation is unavailable.");
                }
            }
            var root = FindRoot();
            destination ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "rea", "dotnet-tool");
            if (File.Exists(destination) || Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
                throw new IOException("Installation directory must be empty or new. Existing installations are preserved; select another --tool-path.");
            if (package is not null && !File.Exists(package)) throw new FileNotFoundException("Local package not found", package);
            await output.WriteLineAsync($"REA install plan\n  Source: {package ?? root + " (build and pack C# CLI)"}\n  Tool directory: {destination}\n  Runtime: .NET 10 (bring your own SDK/runtime)\n  Setup: unavailable; no provider or PATH configuration\n  Restore: local package feed only; no registry publication assumed");
            if (dryRun) { await output.WriteLineAsync("Dry run complete; no installation changes made."); return 0; }
            if (!yes)
            {
                if (args.Contains("--no-prompt", StringComparer.Ordinal)) throw new ArgumentException("Use --yes to approve the displayed plan noninteractively.");
                await output.WriteLineAsync("Install according to this plan? Type yes to continue:");
                if (await input.ReadLineAsync() != "yes") { await output.WriteLineAsync("Installation cancelled; no installation changes made."); return 0; }
            }
            staging = Path.Combine(root, "dotnet", "artifacts", "installer", Guid.NewGuid().ToString("N"));
            var feed = Path.Combine(staging, "feed"); Directory.CreateDirectory(feed);
            if (package is null)
            {
                var project = Path.Combine(root, "dotnet", "src", "Rea.Cli", "Rea.Cli.csproj");
                await Run("dotnet", root, "pack", project, "--configuration", "Release", "--output", feed);
                package = Directory.GetFiles(feed, "*.nupkg").Single();
            }
            else
            {
                var copy = Path.Combine(feed, Path.GetFileName(package)); File.Copy(package, copy); package = copy;
            }
            // Structural inspection and digest only; this does not assert commit provenance.
            var metadata = PackageQualification.Inspect(package, new string('0', 40));
            await output.WriteLineAsync($"  Package: {metadata.PackageId} {metadata.Version}\n  SHA-256: {metadata.Sha256}");
            var config = Path.Combine(staging, "NuGet.Config");
            new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"), new XElement("add", new XAttribute("key", "local"), new XAttribute("value", feed))))).Save(config);
            await Run("dotnet", root, "tool", "install", metadata.PackageId, "--version", metadata.Version, "--tool-path", destination, "--configfile", config, "--no-cache");
            var executable = Path.Combine(destination, OperatingSystem.IsWindows() ? "rea.exe" : "rea");
            await Run(executable, root, "--version");
            await output.WriteLineAsync($"REA installed. Run: \"{executable}\" --help\nAdd this directory to PATH yourself if desired. No setup command was run.");
            Directory.Delete(staging, true); staging = null;
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Xml.XmlException or OperationCanceledException)
        {
            await error.WriteLineAsync("REA installation failed: " + ex.Message);
            if (staging is not null) await error.WriteLineAsync("Retained installation diagnostics/packages: " + staging + ". Any partial tool installation is preserved; inspect it before retrying.");
            return 1;
        }
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Rea.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new IOException("Run the installer from a REA source checkout.");
    }

    private static async Task Run(string command, string root, params string[] arguments)
    {
        var start = new ProcessStartInfo(command) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start " + command);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try { await process.WaitForExitAsync(deadline.Token); }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
        if (process.ExitCode != 0) throw new IOException(command + " exited with code " + process.ExitCode);
    }
}
