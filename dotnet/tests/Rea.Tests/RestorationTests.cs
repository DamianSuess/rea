using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rea.Automation;

namespace Rea.Tests;

internal static class RestorationTests
{
    public static IEnumerable<Case> Cases()
    {
        yield return Case.Sync("All 265 original support paths retain sources or explicit C# replacements", () =>
        {
            var root = Root();
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "docs/folder-restoration.json")))!;
            Check.Equal("ef250b14442ad99458ef7ba1e59b78c809238675", manifest["source_commit"]!.GetValue<string>());
            var entries = manifest["entries"]!.AsArray(); Check.Equal(265, entries.Count);
            Check.Equal(entries.Count, entries.Select(item => item!["original"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count());
            foreach (var entry in entries)
            {
                var path = Path.GetFullPath(Path.Combine(root, entry!["current"]!.GetValue<string>()));
                Check.True(path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                Check.True(File.Exists(path), "Missing restored/replacement path: " + path);
            }
            foreach (var name in new[] { "docs", ".agents", ".github", "bridge", "experiments" }) Check.True(Directory.Exists(Path.Combine(root, name)));
        });
        yield return Case.Sync("Every original workflow is classified and no active workflow invokes removed Node tooling", () =>
        {
            var root = Root(); var directory = Path.Combine(root, ".github/workflows");
            var inventory = JsonNode.Parse(File.ReadAllText(Path.Combine(root, ".github/workflow-compatibility.json")))!;
            var active = inventory["active"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray();
            var unavailable = inventory["unavailable_manual_lanes"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray();
            var actual = Directory.GetFiles(directory, "*.yml").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
            Check.Equal(string.Join(',', actual), string.Join(',', active.Concat(unavailable).Order(StringComparer.Ordinal)));
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, ".github"), "*.yml", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                foreach (var forbidden in new[] { "actions/setup-node", "actions/github-script", "npm ci", "node scripts/", "wrangler-action", "actions/setup-python" })
                    Check.True(!text.Contains(forbidden, StringComparison.Ordinal), $"Removed runtime in {file}: {forbidden}");
            }
            foreach (var file in unavailable)
            {
                var text = File.ReadAllText(Path.Combine(directory, file));
                Check.True(text.Contains("workflow_dispatch:", StringComparison.Ordinal));
                Check.True(text.Contains("Rea.Automation.dll unavailable ", StringComparison.Ordinal));
                Check.True(!text.Contains("pull_request:", StringComparison.Ordinal));
            }
            var readiness = File.ReadAllText(Path.Combine(directory, "pr-readiness.yml"));
            Check.True(readiness.Contains("ref: ${{ github.event.repository.default_branch }}", StringComparison.Ordinal));
            Check.True(!readiness.Contains("pull_request.head", StringComparison.Ordinal));
        });
        yield return Case.Sync("Restored real PR template binds to C# readiness without checking human declarations on disk", () =>
        {
            var template = File.ReadAllText(Path.Combine(Root(), ".github/pull_request_template.md"));
            var body = Regex.Replace(template, @"(?m)^Human owner:.*$", "Human owner: @human-owner", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
                .Replace("- [ ]", "- [x]", StringComparison.Ordinal);
            Check.Equal("success", Readiness.Evaluate(false, body, template).State);
            Check.Equal("failure", Readiness.Evaluate(false, template, template).State);
        });
        yield return new("Empty and failed test reports cannot turn a workflow green", async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "rea-report-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Combine(directory, "report.json");
                foreach (var content in new[] { "{\"failures\":0,\"cases\":[]}", "{\"failures\":0,\"cases\":[{\"passed\":false}]}", "{}" })
                {
                    await File.WriteAllTextAsync(path, content); using var output = new StringWriter(); using var error = new StringWriter();
                    Check.Equal(1, await AutomationCommand.RunAsync(["verify-report", path], output, error));
                }
            }
            finally { Directory.Delete(directory, true); }
        });
    }
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rea.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Root solution missing");
    }
}
