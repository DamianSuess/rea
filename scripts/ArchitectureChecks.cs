using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace Rea.Validation;

public static class ArchitectureChecks
{
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        ["Rea.Domain"] = [],
        ["Rea.Application"] = ["Rea.Domain"],
        ["Rea.Managed"] = ["Rea.Domain", "Rea.Application"],
        ["Rea.Cli"] = ["Rea.Domain", "Rea.Application", "Rea.Managed"]
    };

    public static IEnumerable<ValidationIssue> Inspect(string root)
    {
        var directory = Path.Combine(root, "dotnet/src");
        foreach (var project in Directory.GetFiles(directory, "*.csproj", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(project);
            if (!Allowed.ContainsKey(name)) { yield return new(project, "error", "New application project needs an explicit architecture policy"); continue; }
            var document = XDocument.Load(project);
            foreach (var reference in document.Descendants("ProjectReference"))
            {
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, reference.Attribute("Include")?.Value ?? throw new InvalidDataException("ProjectReference has no Include")));
                if (!File.Exists(target)) yield return new(project, "error", "Missing project dependency: " + target);
                else if (!Permitted(name, Path.GetFileNameWithoutExtension(target))) yield return new(project, "error", "Forbidden project dependency: " + target);
            }
            var assembly = Path.Combine(Path.GetDirectoryName(project)!, "bin/Release/net10.0", name == "Rea.Cli" ? "rea.dll" : name + ".dll");
            if (!File.Exists(assembly)) { yield return new(project, "error", "Build Release before validating compiled dependencies"); continue; }
            using var stream = File.OpenRead(assembly); using var pe = new PEReader(stream); var reader = pe.GetMetadataReader();
            foreach (var handle in reader.AssemblyReferences)
            {
                var dependency = reader.GetString(reader.GetAssemblyReference(handle).Name);
                if ((dependency.StartsWith("Rea.", StringComparison.Ordinal) || dependency == "rea") && !Permitted(name, dependency))
                    yield return new(project, "error", "Forbidden compiled assembly dependency: " + dependency);
            }
        }
        foreach (var name in Allowed.Keys)
            if (!File.Exists(Path.Combine(directory, name, name + ".csproj"))) yield return new(name, "error", "Required architecture project missing");
    }

    public static bool Permitted(string owner, string dependency) => Allowed.TryGetValue(owner, out var permitted) && permitted.Contains(dependency, StringComparer.Ordinal);
}
