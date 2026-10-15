using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml;

namespace Rea.Automation;

public sealed record QualifiedPackage(string SourceSha, string FileName, string Sha256, string PackageId, string Version);

public static class PackageQualification
{
    public static QualifiedPackage Inspect(string package, string sourceSha)
    {
        if (!Regex.IsMatch(sourceSha, @"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new ArgumentException("Exact 40-character source SHA required");
        using var stream = File.OpenRead(package); var digest = Convert.ToHexStringLower(SHA256.HashData(stream)); stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
            if (Path.GetExtension(entry.FullName).ToLowerInvariant() is ".js" or ".jsx" or ".ts" or ".tsx" or ".mjs" or ".cjs" or ".mts" or ".cts" or ".node" or ".py" or ".java" or ".swift" or ".c" or ".cpp" or ".sh" or ".ps1")
                throw new InvalidDataException("Foreign implementation in application package: " + entry.FullName);
        foreach (var file in new[] { "rea.dll", "Rea.Domain.dll", "Rea.Application.dll", "Rea.Managed.dll", "rea.runtimeconfig.json" })
            if (zip.GetEntry("tools/net10.0/any/" + file) is null) throw new InvalidDataException("Missing package dependency: " + file);
        var nuspec = zip.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        if (nuspec.Length > 1024 * 1024) throw new InvalidDataException("Package metadata exceeds the 1 MiB inspection budget");
        using var metadata = nuspec.Open();
        using var xml = XmlReader.Create(metadata, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        var document = XDocument.Load(xml);
        string Value(string name) => document.Descendants().Single(element => element.Name.LocalName == name).Value;
        var id = Value("id"); var version = Value("version");
        if (id != "Rea.DotNet.Preview") throw new InvalidDataException("Unexpected package identity");
        return new(sourceSha, Path.GetFileName(package), digest, id, version);
    }
    public static void Verify(string package, string manifest, string sourceSha)
    {
        var expected = JsonSerializer.Deserialize<QualifiedPackage>(File.ReadAllText(manifest)) ?? throw new InvalidDataException("Invalid package manifest");
        if (Inspect(package, sourceSha) != expected) throw new InvalidDataException("Qualified package identity or source does not match");
    }
}
