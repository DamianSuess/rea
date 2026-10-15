using System.Text.Json;

namespace Rea.Validation;

public static class ValidationCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length < 2) throw new ArgumentException("Usage: Rea.Validation repository ROOT | test-hygiene ROOT REPORT | package ROOT PACKAGE_DIRECTORY");
            var root = Path.GetFullPath(args[1]);
            switch (args[0])
            {
                case "repository" when args.Length == 2:
                    var issues = ArchitectureChecks.Inspect(root).Concat(DocumentationChecks.Inspect(root)).ToArray();
                    var path = Path.Combine(root, "dotnet/artifacts/validation/repository.json");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(issues, new JsonSerializerOptions { WriteIndented = true }));
                    foreach (var issue in issues.Where(item => item.Severity == "error").Take(20)) await error.WriteLineAsync($"{issue.File}: {issue.Message}");
                    await output.WriteLineAsync($"Repository validation: {issues.Count(item => item.Severity == "error")} errors, {issues.Count(item => item.Severity == "historical-unavailable")} historical references unavailable. Report: {path}");
                    return issues.Any(item => item.Severity == "error") ? 1 : 0;
                case "test-hygiene" when args.Length == 3:
                    await ExecutionChecks.TestHygiene(root, Path.GetFullPath(args[2]));
                    await output.WriteLineAsync("Tests, discovery and isolated temporary-resource cleanup verified."); return 0;
                case "package" when args.Length == 3:
                    await ExecutionChecks.Package(root, Path.GetFullPath(args[2]));
                    await output.WriteLineAsync("Installed .NET package: both analysis commands, digests, failure behavior and input preservation verified."); return 0;
                default: throw new ArgumentException("Unknown validation command or argument count");
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Xml.XmlException or JsonException or OperationCanceledException)
        { await error.WriteLineAsync(ex.Message); return 1; }
    }
}

public sealed record ValidationIssue(string File, string Severity, string Message);
