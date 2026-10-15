using System.Text.Json.Nodes;
using Rea.Automation;

namespace Rea.Validation;

public static class DocumentationChecks
{
    private const string HistoricalMarker = "> **C# compatibility status:** Restored from `ef250b14442ad99458ef7ba1e59b78c809238675`.";
    private const string HistoricalSection = "## Historical implementation guidance";

    public static IEnumerable<ValidationIssue> Inspect(string root)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "docs/folder-restoration.json")))!;
        var restored = manifest["entries"]!.AsArray().Select(entry => entry!["current"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var paths = Directory.GetFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories)
            .Concat(new[] { "README.md", "AGENTS.md", "CONTRIBUTING.md", "SECURITY.md", "DISCLAIMER.md", "THIRD_PARTY_NOTICES.md", "dotnet/README.md", ".github/README.md", "bridge/README.md", "experiments/analysis-efficiency/CSharp.md", "scripts/README.md" }.Select(path => Path.Combine(root, path)));
        foreach (var file in paths)
        {
            if (!File.Exists(file)) { yield return new(file, "error", "Required current guide missing"); continue; }
            var text = File.ReadAllText(file); var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var historical = restored.Contains(relative) && text.Contains(HistoricalMarker, StringComparison.Ordinal);
            var active = historical ? text[..text.IndexOf(HistoricalMarker, StringComparison.Ordinal)] + text.Split('\n').First(line => line.Contains(HistoricalMarker, StringComparison.Ordinal)) : text;
            foreach (var issue in CheckLinks(file, active, root)) yield return issue;
            foreach (var issue in CheckCliExamples(file, active)) yield return issue;
            if (historical)
                foreach (var issue in CheckLinks(file, text, root).Where(issue => issue.Severity == "error"))
                    yield return issue with { Severity = "historical-unavailable" };
        }
        // Bind documented supported commands to the executable's public help, separately
        // verified by the installed-package workflow. Historical examples are not contracts.
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        foreach (var command in new[] { "inspect-header", "inspect-managed-artifact" })
            if (!readme.Contains('`' + command + '`', StringComparison.Ordinal)) yield return new("README.md", "error", "Missing supported command: " + command);

        var skills = Path.Combine(root, ".agents/skills");
        foreach (var bundle in Directory.GetDirectories(skills))
        {
            var skill = Path.Combine(bundle, "SKILL.md");
            if (!File.Exists(skill)) { yield return new(bundle, "error", "Skill has no SKILL.md"); continue; }
            var text = File.ReadAllText(skill);
            foreach (var issue in SkillMetadata(skill, text, Path.GetFileName(bundle))) yield return issue;
            foreach (var file in Directory.GetFiles(bundle, "*.md", SearchOption.AllDirectories))
            {
                var content = File.ReadAllText(file);
                var boundary = content.IndexOf(HistoricalSection, StringComparison.Ordinal);
                var historicalReference = file != skill && content.Contains(HistoricalMarker, StringComparison.Ordinal);
                var active = ActiveSkillText(content);
                foreach (var issue in CheckLinks(file, active, bundle)) yield return issue;
                foreach (var issue in CheckCliExamples(file, active)) yield return issue;
                if (boundary >= 0 || historicalReference)
                    foreach (var issue in CheckLinks(file, content, bundle)) yield return issue with { Severity = "historical-unavailable" };
            }
        }
    }

    public static string ActiveSkillText(string content)
    {
        var boundary = content.IndexOf(HistoricalSection, StringComparison.Ordinal);
        if (boundary >= 0) return content[..boundary];
        return content.Contains(HistoricalMarker, StringComparison.Ordinal)
            ? content.Split('\n').First(line => line.Contains(HistoricalMarker, StringComparison.Ordinal)) : content;
    }

    public static IEnumerable<ValidationIssue> SkillMetadata(string file, string text, string expectedName)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!text.StartsWith("---\n", StringComparison.Ordinal))
        { yield return new(file, "error", "Skill frontmatter missing"); yield break; }
        var end = text.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (end < 0)
        { yield return new(file, "error", "Skill frontmatter missing or unterminated"); yield break; }
        var frontmatter = text[4..end];
        foreach (var key in new[] { "name", "description" })
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(frontmatter, "^" + key + @":[ \t]*(.*)$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            var value = matches.Count == 1 ? matches[0].Groups[1].Value.Trim().Trim('"', '\'') : "";
            if (value.Length == 0 || value.StartsWith('#') || value is ">" or ">-" or "|" or "|-" || key == "name" && value != expectedName)
                yield return new(file, "error", "Skill requires one nonempty scalar " + key + (key == "name" ? " matching its bundle directory" : ""));
        }
    }

    public static IEnumerable<ValidationIssue> CheckCliExamples(string file, string markdown)
    {
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(MarkdownFacts.Code(markdown), @"(?m)^[ \t]*(?:dotnet[ \t]+exec[ \t]+(?:[^\s]*/)?rea\.dll|(?:[^\s]*/)?rea(?:\.exe)?)[ \t]+([a-z][a-z0-9-]*)", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            if (match.Groups[1].Value is not ("inspect-header" or "inspect-managed-artifact"))
                yield return new(file, "error", "Executable example advertises an unavailable C# command: " + match.Groups[1].Value);
    }

    public static IEnumerable<ValidationIssue> CheckLinks(string file, string markdown, string allowedRoot)
    {
        allowedRoot = Path.GetFullPath(allowedRoot);
        foreach (var link in MarkdownFacts.Links(markdown))
        {
            if (link.StartsWith("missing-reference:", StringComparison.Ordinal)) { yield return new(file, "error", link); continue; }
            if (Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto") continue;
            var parts = link.Split('#', 2); var path = Uri.UnescapeDataString(parts[0].Split('?', 2)[0]);
            var target = path.Length == 0 ? file : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, path));
            if (!target.StartsWith(allowedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            { yield return new(file, "error", "Reference escapes its document/skill root: " + link); continue; }
            if (!File.Exists(target) && !Directory.Exists(target)) { yield return new(file, "error", "Missing local reference: " + link); continue; }
            if (parts.Length == 2 && parts[1].Length != 0 && Path.GetExtension(target) == ".md")
            {
                var content = target == file ? markdown : File.ReadAllText(target);
                if (!MarkdownFacts.Headings(content).Contains(Uri.UnescapeDataString(parts[1]), StringComparer.Ordinal))
                    yield return new(file, "error", "Missing heading fragment: " + link);
            }
        }
    }
}
