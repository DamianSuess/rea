using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Rea.Automation;

// Shared Markdown subset used by the static renderer and repository validation.
// Fenced/inline code and HTML comments do not produce links or headings.
public static class MarkdownFacts
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);
    public static string Slug(string text)
    {
        text = WebUtility.HtmlDecode(text).ToLowerInvariant().Replace("`", "", StringComparison.Ordinal);
        return string.Concat(text.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' || char.IsWhiteSpace(character))).Replace(' ', '-');
    }
    public static string UniqueSlug(string text, IDictionary<string, int> counts)
    {
        var slug = Slug(text); counts.TryGetValue(slug, out var count);
        var candidate = count == 0 ? slug : slug + "-" + count.ToString(CultureInfo.InvariantCulture);
        while (counts.ContainsKey(candidate)) { count++; candidate = slug + "-" + count.ToString(CultureInfo.InvariantCulture); }
        counts[slug] = count + 1; counts[candidate] = Math.Max(counts.TryGetValue(candidate, out var used) ? used : 0, 1);
        return candidate;
    }
    public static string WithoutComments(string markdown) => Regex.Replace(markdown, @"<!--[\s\S]*?-->", "", RegexOptions.CultureInvariant, Deadline);
    public static string Body(string markdown)
    {
        markdown = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (markdown.StartsWith("---\n", StringComparison.Ordinal))
        {
            var end = markdown.IndexOf("\n---\n", 4, StringComparison.Ordinal);
            if (end >= 0) markdown = markdown[(end + 5)..];
        }
        return WithoutComments(markdown);
    }
    public static string Prose(string markdown) => string.Join('\n', Lines(markdown).Where(item => !item.IsCode).Select(item => item.Line));
    public static string Code(string markdown) => string.Join('\n', Lines(markdown).Where(item => item.IsCode).Select(item => item.Line));
    private static IEnumerable<(string Line, bool IsCode)> Lines(string markdown)
    {
        markdown = Body(markdown);
        char? fence = null; var length = 0;
        foreach (var line in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var marker = Regex.Match(line, @"^ {0,3}(`{3,}|~{3,})", RegexOptions.CultureInvariant, Deadline);
            if (fence is not null)
            {
                if (marker.Success && marker.Value.Trim()[0] == fence && marker.Groups[1].Length >= length && line[marker.Length..].Trim().Length == 0) fence = null;
                else yield return (line, true);
                continue;
            }
            if (marker.Success) { fence = marker.Groups[1].Value[0]; length = marker.Groups[1].Length; continue; }
            yield return (line, line.StartsWith("    ", StringComparison.Ordinal) || line.StartsWith('\t'));
        }
    }
    public static string[] Headings(string markdown)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        return Regex.Matches(Prose(markdown), @"^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant, Deadline)
            .Select(match => UniqueSlug(match.Groups[1].Value, counts)).ToArray();
    }
    public static string[] Links(string markdown)
    {
        var prose = Regex.Replace(Prose(markdown), @"(`+).*?\1", "", RegexOptions.CultureInvariant, Deadline);
        var links = Regex.Matches(prose, @"!?\[[^\]\n]*\]\(\s*(?:<([^>]+)>|([^\s)]+))(?:\s+""[^""]*"")?\s*\)", RegexOptions.CultureInvariant, Deadline)
            .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).ToList();
        var definitions = Regex.Matches(prose, @"^ {0,3}\[([^\]]+)\]:\s*<?([^\s>]+)>?", RegexOptions.Multiline | RegexOptions.CultureInvariant, Deadline)
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value, StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(prose, @"!?\[([^\]\n]+)\]\[([^\]\n]*)\]", RegexOptions.CultureInvariant, Deadline))
        {
            var label = match.Groups[2].Length == 0 ? match.Groups[1].Value : match.Groups[2].Value;
            links.Add(definitions.TryGetValue(label, out var target) ? target : "missing-reference:" + label);
        }
        return links.ToArray();
    }
}
