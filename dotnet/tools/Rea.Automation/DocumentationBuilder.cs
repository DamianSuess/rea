using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rea.Automation;

public static class DocumentationBuilder
{
    public static int Build(string root, string destination)
    {
        root = Path.GetFullPath(root); destination = Path.GetFullPath(destination);
        var source = Path.Combine(root, "docs");
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("docs folder is missing");
        if (destination == source || source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Documentation output cannot overwrite its source tree");
        Directory.CreateDirectory(destination);
        var config = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(source, "site.json")));
        var title = config.GetProperty("title").GetString() ?? throw new InvalidDataException("Documentation title missing");
        var pages = Directory.EnumerateFiles(source, "*.md", new EnumerationOptions
        { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).Order(StringComparer.Ordinal).ToArray();
        if (pages.Length == 0) throw new InvalidDataException("No documentation pages discovered");
        var navigation = new StringBuilder("<nav><a href=\"index.html\">C# documentation</a><ul>");
        foreach (var page in pages)
        {
            var relative = Path.GetRelativePath(source, page).Replace('\\', '/');
            navigation.Append("<li><a href=\"").Append(E(HtmlPath(relative))).Append("\">").Append(E(relative)).Append("</a></li>");
        }
        navigation.Append("</ul></nav>");
        foreach (var page in pages)
        {
            var relative = Path.GetRelativePath(source, page);
            var output = Path.Combine(destination, HtmlPath(relative));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var markdown = File.ReadAllText(page);
            File.WriteAllText(output, "<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>" + E(title) + "</title>" +
                "<style>body{max-width:1000px;margin:2rem auto;padding:1rem;font:16px/1.6 system-ui}pre{overflow:auto;background:#f3f4f6;padding:1rem}img{max-width:100%}a{color:#1554a3}</style>" +
                "<body><p><strong>REA C# edition.</strong> Historical guides are labelled; they do not establish current provider support.</p>" +
                Render(markdown) + "</body></html>", Encoding.UTF8);
        }
        // Dedicated landing page is current; original guide filenames remain intact.
        File.WriteAllText(Path.Combine(destination, "index.html"), "<!doctype html><html><meta charset=\"utf-8\"><title>REA C# documentation</title><body><h1>REA C# documentation</h1><p>Current commands: inspect-header and inspect-managed-artifact. Provider bridges are source-only integrations.</p>" + navigation + "</body></html>", Encoding.UTF8);
        foreach (var asset in Directory.EnumerateFiles(source, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            if (Path.GetExtension(asset).ToLowerInvariant() is not (".png" or ".svg" or ".csv" or ".json")) continue;
            if (Path.GetExtension(asset) == ".svg" && File.ReadAllText(asset).Contains("<script", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Executable SVG scripts are not supported");
            var output = Path.Combine(destination, Path.GetRelativePath(source, asset));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.Copy(asset, output, true);
        }
        File.WriteAllText(Path.Combine(destination, "build.json"), JsonSerializer.Serialize(new { generator = "Rea.Automation", pages = pages.Length, javascript = false }));
        return pages.Length;
    }

    private static string HtmlPath(string path) => Path.ChangeExtension(path, ".html");
    private static string E(string text) => WebUtility.HtmlEncode(text);

    public static string Render(string markdown)
    {
        var result = new StringBuilder(); char? fence = null; var fenceLength = 0;
        foreach (var line in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var opening = Regex.Match(line, @"^ {0,3}(`{3,}|~{3,})", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (fence is not null)
            {
                if (opening.Success && opening.Groups[1].Value[0] == fence && opening.Groups[1].Length >= fenceLength)
                { result.Append("</code></pre>"); fence = null; }
                else result.Append(E(line)).Append('\n');
                continue;
            }
            if (opening.Success) { result.Append("<pre><code>"); fence = opening.Groups[1].Value[0]; fenceLength = opening.Groups[1].Length; continue; }
            var heading = Regex.Match(line, @"^(#{1,6})\s+(.+)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (heading.Success)
            { var level = heading.Groups[1].Length; result.Append($"<h{level}>").Append(Inline(heading.Groups[2].Value)).Append($"</h{level}>"); }
            else if (line.Length > 0) result.Append("<p>").Append(Inline(line)).Append("</p>");
        }
        if (fence is not null) result.Append("</code></pre>");
        return result.ToString();
    }

    private static string Inline(string source)
    {
        var output = new StringBuilder(); var offset = 0;
        foreach (Match match in Regex.Matches(source, @"(!?)\[([^\]]*)\]\(([^\s)]+)\)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            output.Append(E(source[offset..match.Index])); var uri = match.Groups[3].Value;
            var safe = !uri.StartsWith("//", StringComparison.Ordinal) &&
                (!Uri.TryCreate(uri, UriKind.Absolute, out var absolute) || absolute.Scheme is "https" or "http");
            if (!safe) output.Append(E(match.Value));
            else if (match.Groups[1].Value == "!") output.Append("<img alt=\"").Append(E(match.Groups[2].Value)).Append("\" src=\"").Append(E(uri)).Append("\">");
            else
            {
                uri = Regex.Replace(uri, @"\.md(?=#|$)", ".html", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                output.Append("<a href=\"").Append(E(uri)).Append("\">").Append(E(match.Groups[2].Value)).Append("</a>");
            }
            offset = match.Index + match.Length;
        }
        return output.Append(E(source[offset..])).ToString();
    }
}
