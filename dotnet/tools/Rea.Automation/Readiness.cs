using System.Text.RegularExpressions;

namespace Rea.Automation;

public sealed record ReadinessVerdict(string State, string Description);

public static class Readiness
{
    private static Match Match(string text, string pattern) => Regex.Match(text, pattern,
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static bool ConventionalTitle(string title) => Match(title,
        @"\A(?:feat|fix|docs|refactor|perf|test|build|ci|chore|revert)(?:\([a-z0-9._/-]+\))?!?: [^\r\n]+\z").Success;

    public static ReadinessVerdict Evaluate(bool draft, string body, string template)
    {
        if (draft) return new("pending", "Draft PR: complete Author readiness before requesting review.");
        var expected = Lines(template).Select(line => Match(line, @"^- \[ \] (.+)$"))
            .Where(match => match.Success).Select(match => match.Groups[1].Value).ToArray();
        if (expected.Length != 3 || expected.Distinct(StringComparer.Ordinal).Count() != 3)
            throw new InvalidDataException("Trusted PR template must contain three distinct readiness declarations.");
        var lines = Lines(body);
        var owners = lines.Where(line => line.StartsWith("Human owner:", StringComparison.Ordinal)).ToArray();
        if (owners.Length != 1 || !Match(owners[0], @"^Human owner: @[a-zA-Z\d](?:[a-zA-Z\d-]*[a-zA-Z\d])?$").Success)
            return new("failure", "Fill Human owner with one @GitHub-handle in Author readiness.");
        var declarations = lines.Select(line => Match(line, @"^- \[([ xX])\] (.+)$")).Where(match => match.Success).ToArray();
        foreach (var text in expected)
        {
            var matches = declarations.Where(match => match.Groups[2].Value == text).ToArray();
            if (matches.Length != 1 || !string.Equals(matches[0].Groups[1].Value, "x", StringComparison.OrdinalIgnoreCase))
                return new("failure", "Complete all three Author readiness declarations from the current PR template.");
        }
        return new("success", "Human owner and all three initial readiness declarations are filled.");
    }

    private static string[] Lines(string markdown)
    {
        var visible = new List<string>(); var comment = false; char? fence = null; var fenceLength = 0;
        foreach (var source in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (fence is not null)
            {
                if (Match(source, "^ {0,3}" + Regex.Escape(fence.Value.ToString()) + "{" + fenceLength + ",}\\s*$").Success) fence = null;
                continue;
            }
            var opening = !comment ? Match(source, @"^ {0,3}(`{3,}|~{3,})") : Match("", "never");
            if (opening.Success) { fence = opening.Groups[1].Value[0]; fenceLength = opening.Groups[1].Length; continue; }
            var line = ""; var offset = 0;
            while (offset < source.Length)
            {
                var marker = comment ? "-->" : "<!--";
                var index = source.IndexOf(marker, offset, StringComparison.Ordinal);
                if (!comment) line += source[offset..(index < 0 ? source.Length : index)];
                if (index < 0) break;
                offset = index + marker.Length; comment = !comment;
            }
            visible.Add(line.TrimEnd());
        }
        var starts = visible.Select((line, index) => (line, index)).Where(item => Match(item.line, @"^## Author readiness\s*$").Success).ToArray();
        if (starts.Length != 1) return [];
        return visible.Skip(starts[0].index + 1).TakeWhile(line => !Match(line, @"^#{1,2}\s").Success).ToArray();
    }
}
