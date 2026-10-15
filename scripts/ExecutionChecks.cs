using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Rea.Validation;

public static class ExecutionChecks
{
    public static async Task TestHygiene(string root, string report)
    {
        var temporary = Workspace(root, "test-hygiene");
        var environment = new Dictionary<string, string> { ["TMP"] = temporary, ["TEMP"] = temporary, ["TMPDIR"] = temporary };
        var test = Path.Combine(root, "dotnet/tests/Rea.Tests/bin/Release/net10.0/Rea.Tests.dll");
        // Unique output makes stale reports impossible without deleting local evidence.
        var runReport = temporary + ".json";
        var result = await Run(root, "dotnet", ["exec", test, "--report", runReport], environment);
        if (File.Exists(runReport))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            if (File.Exists(report)) File.Copy(report, report + ".backup-" + Guid.NewGuid().ToString("N"));
            File.Copy(runReport, report, true);
        }
        var residues = Directory.GetFileSystemEntries(temporary);
        var diagnostic = Path.Combine(root, "dotnet/artifacts/validation/hygiene.json");
        var hygiene = JsonSerializer.Serialize(new { result.ExitCode, result.ElapsedMs, result.CpuMs, result.PeakWorkingSetBytes, residues, temporary, process_family_costs = "not measured; counters describe the direct test process" }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(temporary + ".hygiene.json", hygiene);
        await File.WriteAllTextAsync(diagnostic, hygiene);
        if (result.ExitCode != 0 || residues.Length != 0)
            throw new InvalidDataException($"Test/hygiene gate failed (exit {result.ExitCode}); retained {temporary}; residues: {string.Join(", ", residues)}\n{result.Error}\n{result.Output}");
        RequireEmpty(temporary);
        using var document = JsonDocument.Parse(File.ReadAllText(runReport));
        var cases = document.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        if (document.RootElement.GetProperty("failures").GetInt32() != 0 || cases.Length == 0 || cases.Any(item => !item.GetProperty("passed").GetBoolean()))
            throw new InvalidDataException("No passing executed test report");
        Directory.Delete(temporary);
    }

    public static async Task Package(string root, string directory)
    {
        var packages = Directory.GetFiles(directory, "*.nupkg");
        if (packages.Length != 1) throw new InvalidDataException("Exactly one preview package required");
        var workspace = Workspace(root, "installed-package");
        try
        {
            var home = Path.Combine(workspace, "home"); Directory.CreateDirectory(home);
            var environment = new Dictionary<string, string>
            {
                ["DOTNET_CLI_HOME"] = home, ["NUGET_PACKAGES"] = Path.Combine(workspace, "nuget"),
                ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1", ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
            };
            var feed = Path.Combine(workspace, "feed"); Directory.CreateDirectory(feed);
            var package = Path.Combine(feed, Path.GetFileName(packages[0])); File.Copy(packages[0], package);
            // No commit provenance is asserted by the structural metadata inspection.
            var metadata = Rea.Automation.PackageQualification.Inspect(package, new string('0', 40));
            var config = Path.Combine(workspace, "NuGet.Config");
            new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"), new XElement("add", new XAttribute("key", "local"), new XAttribute("value", feed))))).Save(config);
            var tool = Path.Combine(workspace, "tool");
            await Success(root, "dotnet", ["tool", "install", metadata.PackageId, "--version", metadata.Version, "--tool-path", tool, "--configfile", config, "--no-cache"], environment);
            var cli = Path.Combine(tool, OperatingSystem.IsWindows() ? "rea.exe" : "rea");
            var help = await Success(workspace, cli, ["--help"], environment);
            var measurements = new List<object> { new { command = "help", help.ExitCode, help.ElapsedMs, help.CpuMs, help.PeakWorkingSetBytes } };
            foreach (var command in new[] { "inspect-header", "inspect-managed-artifact" })
                if (!help.Output.Contains(command, StringComparison.Ordinal)) throw new InvalidDataException("Installed CLI help omitted " + command);
            var fixture = Path.Combine(workspace, "fixture with spaces.dll");
            File.Copy(Path.Combine(root, "dotnet/tests/fixtures/Rea.ManagedFixture/bin/Release/net10.0/Rea.ManagedFixture.dll"), fixture);
            var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(fixture)));
            foreach (var command in new[] { "inspect-header", "inspect-managed-artifact" })
            {
                var arguments = command == "inspect-header" ? new[] { command, fixture, "--header-bytes", new FileInfo(fixture).Length.ToString(System.Globalization.CultureInfo.InvariantCulture) } : new[] { command, fixture };
                var result = await Success(workspace, cli, arguments, environment);
                ValidateInspection(command, result.Output, digest);
                measurements.Add(new { command, result.ExitCode, result.ElapsedMs, result.CpuMs, result.PeakWorkingSetBytes });
                if (Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(fixture))) != digest) throw new InvalidDataException("Analysis modified input");
            }
            var missing = await Run(workspace, cli, ["inspect-managed-artifact", Path.Combine(workspace, "absent.dll")], environment);
            var syntax = await Run(workspace, cli, ["not-a-command"], environment);
            if (missing.ExitCode != 1 || syntax.ExitCode != 2) throw new InvalidDataException("Installed CLI lost expected failure exit codes");
            using (var failure = JsonDocument.Parse(missing.Output))
                if (failure.RootElement.GetProperty("ok").GetBoolean()) throw new InvalidDataException("Missing input reported success");
            await File.WriteAllTextAsync(Path.Combine(workspace, "verification.json"), JsonSerializer.Serialize(new { metadata.PackageId, metadata.Version, metadata.Sha256, fixture_sha256 = digest, commands = 2, input_preserved = true, installed_package = true, measurements, unmeasured = "process-family costs, tokens, cold/warm cache separation and a JavaScript baseline; no performance improvement claimed" }));
        }
        catch (Exception ex) { throw new InvalidDataException("Installed-package verification failed; evidence retained at " + workspace + ": " + ex.Message, ex); }
        // Retain evidence intentionally under ignored artifacts; hygiene only governs isolated test TEMP.
    }

    public static void ValidateInspection(string command, string output, string digest)
    {
        using var document = JsonDocument.Parse(output); var json = document.RootElement;
        if (!Field(json, "ok").GetBoolean()) throw new InvalidDataException("Installed command reported failure");
        var value = Field(json, "value");
        if (command == "inspect-managed-artifact")
        {
            if (Field(Field(value, "assembly"), "name").GetString() != "Rea.ManagedFixture" || Field(Field(value, "artifact"), "sha256").GetString() != digest)
                throw new InvalidDataException("Installed managed command returned wrong identity/digest");
        }
        else if (command != "inspect-header") throw new ArgumentException("Unknown package-verification command");
        // The package scenario explicitly retains all fixture bytes for the header command.
        else if (Field(Field(value, "header"), "format").GetString() != "pe" || Field(Field(value, "source"), "sha256").GetString() != digest)
            throw new InvalidDataException("Installed header command returned wrong format/digest");
    }
    private static JsonElement Field(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result)
        ? result : throw new InvalidDataException("Analysis output omitted required field: " + name);

    public static void RequireEmpty(string directory)
    {
        if (Directory.EnumerateFileSystemEntries(directory).Any()) throw new InvalidDataException("Owned temporary resources remain: " + directory);
    }

    private static string Workspace(string root, string name)
    {
        var path = Path.Combine(root, "dotnet/artifacts/validation", name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }

    private static async Task<ProcessResult> Success(string root, string command, string[] args, IReadOnlyDictionary<string, string> environment)
    {
        var result = await Run(root, command, args, environment);
        if (result.ExitCode != 0) throw new InvalidDataException($"{command} failed ({result.ExitCode}): {result.Error}\n{result.Output}");
        return result;
    }
    public static async Task<ProcessResult> Run(string root, string command, string[] args, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(command) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        if (environment is not null) foreach (var entry in environment) start.Environment[entry.Key] = entry.Value;
        using var process = Process.Start(start) ?? throw new IOException("Cannot start " + command);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var watch = Stopwatch.StartNew();
        var output = Read(process.StandardOutput, timeout); var error = Read(process.StandardError, timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            double? cpu = null; long? memory = null;
            try { cpu = process.TotalProcessorTime.TotalMilliseconds; memory = process.PeakWorkingSet64; } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException) { }
            return new(process.ExitCode, await output, await error, watch.Elapsed.TotalMilliseconds, cpu, memory);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            try { await Task.WhenAll(output, error); } catch (Exception ex) when (ex is OperationCanceledException or InvalidDataException or IOException) { }
        }
    }
    private static async Task<string> Read(StreamReader stream, CancellationTokenSource timeout)
    {
        var text = new StringBuilder(); var buffer = new char[4096]; int count;
        while ((count = await stream.ReadAsync(buffer.AsMemory(), timeout.Token)) != 0)
        {
            if (text.Length + count > 8 * 1024 * 1024) { timeout.Cancel(); throw new InvalidDataException("Validation subprocess exceeded the 8 Mi-character output budget"); }
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}
public sealed record ProcessResult(int ExitCode, string Output, string Error, double ElapsedMs, double? CpuMs, long? PeakWorkingSetBytes);
