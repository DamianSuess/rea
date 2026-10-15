using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace Rea.Tests;

internal static class PackageTests
{
    public static IEnumerable<Case> Cases(string directory)
    {
        var paths = Directory.GetFiles(directory, "*.nupkg").Order(StringComparer.Ordinal).ToArray();
        yield return Case.Sync("Preview packages exist", () => Check.True(paths.Length > 0, "No packages were produced"));
        foreach (var path in paths)
            yield return Case.Sync($"Package is C#/.NET-only: {Path.GetFileName(path)}", () =>
            {
                using var package = ZipFile.OpenRead(path);
                var names = package.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
                foreach (var name in names)
                {
                    var extension = Path.GetExtension(name);
                    Check.True(extension is not (".js" or ".jsx" or ".ts" or ".tsx" or ".mjs" or ".cjs" or ".mts" or ".cts" or ".node" or ".py" or ".java" or ".swift"), $"Forbidden runtime source or binding: {name}");
                    Check.True(!name.Contains("node_modules", StringComparison.OrdinalIgnoreCase));
                    Check.True(!name.Contains("ManagedFixture", StringComparison.Ordinal));
                }
                foreach (var file in new[] { "rea.dll", "Rea.Application.dll", "Rea.Domain.dll", "Rea.Managed.dll", "rea.deps.json", "rea.runtimeconfig.json", "DotnetToolSettings.xml" })
                    Check.True(names.Contains("tools/net10.0/any/" + file), $"Missing tool dependency: {file}");
                Check.True(names.Contains("README.md"));
                using var configStream = package.GetEntry("tools/net10.0/any/rea.runtimeconfig.json")!.Open();
                using var config = JsonDocument.Parse(configStream);
                Check.Equal("net10.0", config.RootElement.GetProperty("runtimeOptions").GetProperty("tfm").GetString());
                using var settingsStream = package.GetEntry("tools/net10.0/any/DotnetToolSettings.xml")!.Open();
                var command = XDocument.Load(settingsStream).Descendants("Command").Single();
                Check.Equal("rea", command.Attribute("Name")!.Value); Check.Equal("rea.dll", command.Attribute("EntryPoint")!.Value);
            });
    }
}
