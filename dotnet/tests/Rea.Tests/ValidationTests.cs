using Rea.Automation;
using Rea.Validation;

namespace Rea.Tests;

internal static class ValidationTests
{
    public static IEnumerable<Case> Cases()
    {
        yield return Case.Sync("Discovery rejects missing, unexpected, duplicate and empty inventories", () =>
        {
            TestDiscovery.ValidateInventory(["OneTests"], ["OneTests"]);
            Reject(() => TestDiscovery.ValidateInventory(["OneTests", "TwoTests"], ["OneTests"]));
            Reject(() => TestDiscovery.ValidateInventory(["OneTests"], ["OneTests", "ExtraTests"]));
            Reject(() => TestDiscovery.ValidateInventory(["OneTests"], ["OneTests", "OneTests"]));
            Reject(() => TestDiscovery.ValidateInventory([], []));
            Reject(() => TestDiscovery.ValidateCaseNames(["same", "same"]));
            Reject(() => TestDiscovery.ValidateCaseNames([]));
        });
        yield return Case.Sync("Architecture gate detects a forbidden dependency in real project metadata", () =>
        {
            using var fixture = new Fixture(); var root = Root();
            foreach (var name in new[] { "Rea.Domain", "Rea.Application", "Rea.Managed", "Rea.Cli" })
            {
                var directory = Path.Combine(fixture.Path, "dotnet/src", name); Directory.CreateDirectory(Path.Combine(directory, "bin/Release/net10.0"));
                File.Copy(Path.Combine(root, "dotnet/src", name, name + ".csproj"), Path.Combine(directory, name + ".csproj"));
                var assembly = name == "Rea.Cli" ? "rea.dll" : name + ".dll";
                File.Copy(Path.Combine(root, "dotnet/src", name, "bin/Release/net10.0", assembly), Path.Combine(directory, "bin/Release/net10.0", assembly));
            }
            Check.Equal(0, ArchitectureChecks.Inspect(fixture.Path).Count());
            var project = Path.Combine(fixture.Path, "dotnet/src/Rea.Domain/Rea.Domain.csproj");
            var xml = System.Xml.Linq.XDocument.Load(project);
            xml.Root!.Add(new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("ProjectReference", new System.Xml.Linq.XAttribute("Include", "../Rea.Application/Rea.Application.csproj")))); xml.Save(project);
            Check.True(ArchitectureChecks.Inspect(fixture.Path).Any(issue => issue.Message.StartsWith("Forbidden project dependency", StringComparison.Ordinal)));
            Check.True(!ArchitectureChecks.Permitted("Rea.Domain", "Rea.Validation"));
        });
        yield return Case.Sync("Documentation gate rejects broken references and fragments but ignores code examples", () =>
        {
            using var fixture = new Fixture(); var file = Path.Combine(fixture.Path, "guide.md");
            var text = "# Title\n# Title\n[inline](#title)\n[reference][ok]\n[ok]: #title-1\n```text\n[example](absent.md)\n```\n`[inline code](absent.md)`\n";
            File.WriteAllText(file, text);
            Check.Equal(0, DocumentationChecks.CheckLinks(file, text, fixture.Path).Count());
            foreach (var broken in new[] { "[bad](missing.md)", "[bad](#missing)", "[bad](../escape.md)", "[bad][undefined]" })
                Check.True(DocumentationChecks.CheckLinks(file, text + broken, fixture.Path).Any(issue => issue.Severity == "error"));
            Check.True(DocumentationBuilder.Render("# Title\n# Title").Contains("id=\"title-1\"", StringComparison.Ordinal));
            var collision = MarkdownFacts.Headings("# Title\n# Title\n# Title-1");
            Check.Equal(collision.Length, collision.Distinct(StringComparer.Ordinal).Count());
            Check.Equal(0, DocumentationChecks.CheckCliExamples(file, "The old `rea setup` is unavailable.\n```sh\nrea inspect-header PATH\n```").Count());
            Check.True(DocumentationChecks.CheckCliExamples(file, "```text\ndotnet exec rea.dll obsolete-command\n```").Any());
            Check.Equal(0, DocumentationChecks.CheckCliExamples(file, "---\n    link: https://example.invalid/rea\n    details: metadata is not code\n---\n# Guide\n").Count());
        });
        yield return Case.Sync("Active skill guides are portable and exact copies of their owned source", () =>
        {
            var root = Root(); var source = File.ReadAllBytes(Path.Combine(root, "scripts/skill-csharp-guide.md"));
            foreach (var bundle in Directory.GetDirectories(Path.Combine(root, ".agents/skills")))
            {
                using var fixture = new Fixture();
                foreach (var file in Directory.GetFiles(bundle, "*.md", SearchOption.AllDirectories))
                {
                    var destination = Path.Combine(fixture.Path, Path.GetRelativePath(bundle, file)); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!); File.Copy(file, destination);
                }
                var guide = Path.Combine(fixture.Path, "references/current-csharp.md");
                Check.True(source.SequenceEqual(File.ReadAllBytes(guide)), "Generated skill guide drift: " + bundle);
                foreach (var file in Directory.GetFiles(fixture.Path, "*.md", SearchOption.AllDirectories))
                {
                    var active = DocumentationChecks.ActiveSkillText(File.ReadAllText(file));
                    Check.Equal(0, DocumentationChecks.CheckLinks(file, active, fixture.Path).Count());
                }
            }
        });
        yield return Case.Sync("Cleanup gate catches deliberately leaked temporary files", () =>
        {
            using var fixture = new Fixture(); ExecutionChecks.RequireEmpty(fixture.Path);
            File.WriteAllText(Path.Combine(fixture.Path, "leaked.txt"), "owned test residue");
            Reject(() => ExecutionChecks.RequireEmpty(fixture.Path));
        });
        yield return Case.Sync("Skill metadata gate rejects missing duplicate and misplaced declarations", () =>
        {
            Check.Equal(0, DocumentationChecks.SkillMetadata("SKILL.md", "---\nname: sample\ndescription: Valid\n---\n", "sample").Count());
            foreach (var text in new[] { "name: sample\ndescription: fake outside YAML", "---\nname: sample\n---\ndescription: outside", "---\nname: wrong\ndescription: valid\n---\n", "---\nname: sample\nname: sample\ndescription: valid\n---\n" })
                Check.True(DocumentationChecks.SkillMetadata("SKILL.md", text, "sample").Any());
        });
        yield return Case.Sync("Installed-package oracles reject success with incorrect analysis identity", () =>
        {
            Reject(() => ExecutionChecks.ValidateInspection("inspect-managed-artifact", "{}", "expected"));
            Reject(() => ExecutionChecks.ValidateInspection("inspect-managed-artifact", "{\"ok\":false}", "wrong"));
            Reject(() => ExecutionChecks.ValidateInspection("inspect-managed-artifact", "{\"ok\":true,\"value\":{\"assembly\":{\"name\":\"Wrong\"},\"artifact\":{\"sha256\":\"wrong\"}}}", "expected"));
            Reject(() => ExecutionChecks.ValidateInspection("inspect-header", "{\"ok\":true,\"value\":{\"header\":{\"format\":\"elf\"},\"source\":{\"sha256\":\"wrong\"}}}", "expected"));
        });
    }
    private static void Reject(Action action)
    {
        var failed = false; try { action(); } catch (InvalidDataException) { failed = true; }
        Check.True(failed, "Gate accepted an intentional validation failure");
    }
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rea.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidDataException("Missing repository");
    }
    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rea-validation-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
