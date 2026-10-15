namespace Rea.Tests;

internal static class RepositoryTests
{
    public static IEnumerable<Case> Cases()
    {
        yield return Case.Sync("Core/orchestration is C# with only the approved engine-bridge source exceptions", () =>
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rea.slnx"))) directory = directory.Parent;
            Check.True(directory is not null, "Cannot locate repository root solution");
            var violations = FindViolations(directory!.FullName).ToArray();
            Check.True(violations.Length == 0, "Repository language/bridge policy violations: " + string.Join(", ", violations));
        });
        yield return Case.Sync("Repository policy rejects legacy languages and build manifests", () =>
        {
            foreach (var name in new[] { "main.ts", "main.js", "main.mjs", "main.cjs", "main.mts", "main.cts", "main.py", "main.sh", "main.java", "main.cpp", "main.swift", "main.ps1", "package.json", "package-lock.json", "tsconfig.build.json", ".gitmodules", "CMakeLists.txt" })
                Check.True(!Allowed(name), $"Foreign implementation or tooling was permitted: {name}");
            foreach (var name in new[] { "Main.cs", "Rea.csproj", "Rea.slnx", "global.json", "Directory.Build.props", "README.md", "LICENSE", "build.yml", "payload.txt" })
                Check.True(Allowed(name), $"C# code or supporting data was rejected: {name}");
        });
    }

    private static readonly HashSet<string> DataExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".cs", ".csproj", ".slnx", ".props", ".targets", ".json", ".xml", ".yaml", ".yml", ".md", ".txt", ".mermaid" };
    private static readonly HashSet<string> ForbiddenManifests = new(StringComparer.OrdinalIgnoreCase)
    { "package.json", "package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml", "pnpm-workspace.yaml", "bun.lock", "bun.lockb", "deno.json", "deno.jsonc", "turbo.json", "CMakeLists.txt", ".gitmodules" };
    private static readonly HashSet<string> MetadataNames = new(StringComparer.OrdinalIgnoreCase)
    { ".gitignore", ".gitattributes", ".editorconfig", "LICENSE" };

    private static bool Allowed(string name) => !ForbiddenManifests.Contains(name)
        && !name.StartsWith("tsconfig", StringComparison.OrdinalIgnoreCase)
        && !name.StartsWith("jsconfig", StringComparison.OrdinalIgnoreCase)
        && (DataExtensions.Contains(Path.GetExtension(name)) || MetadataNames.Contains(name));

    private static IEnumerable<string> FindViolations(string root)
    {
        var pending = new Stack<string>(); pending.Push(root);
        var packages = Path.GetFullPath(Path.Combine(root, "dotnet", "artifacts"));
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "bridge", "source-manifest.json")));
        var bridges = manifest.RootElement.GetProperty("files").EnumerateArray().ToDictionary(
            file => file.GetProperty("path").GetString()!, file => file.GetProperty("sha256").GetString()!, StringComparer.Ordinal);
        var examinedBridges = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryPop(out var current))
        {
            foreach (var file in Directory.EnumerateFiles(current))
            {
                if (current == root && Path.GetFileName(file) == ".git") continue;
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative == "install.sh")
                {
                    var launcher = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal);
                    if (launcher != "#!/bin/sh\n{\n# Compatibility launcher only; all installation behavior lives in C#.\nset -eu\nroot=$(CDPATH= cd -- \"$(dirname -- \"$0\")\" && pwd)\nexec dotnet run --project \"$root/dotnet/tools/Rea.Installer/Rea.Installer.csproj\" --configuration Release -- \"$@\"\n}\n")
                        yield return relative + " (only the C# compatibility launcher is permitted)";
                    continue;
                }
                if (bridges.TryGetValue(relative, out var expected))
                {
                    if (!relative.StartsWith("bridge/", StringComparison.Ordinal) || Path.GetExtension(file) is not (".py" or ".java" or ".swift"))
                        yield return relative + " (invalid engine exception)";
                    else if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))) != expected)
                        yield return relative + " (bridge source differs from its restoration receipt)";
                    examinedBridges.Add(relative); continue;
                }
                if (relative.StartsWith("docs/", StringComparison.Ordinal) && Path.GetExtension(file) is ".png" or ".svg" or ".csv") continue;
                if (!Allowed(Path.GetFileName(file))) yield return relative;
            }
            foreach (var child in Directory.EnumerateDirectories(current))
            {
                var name = Path.GetFileName(child);
                if (current == root && name is ".git" or ".codex" or ".vs" or ".idea" or ".vscode" or ".cache" or "_reference") continue;
                if (string.Equals(child, packages, StringComparison.OrdinalIgnoreCase)) continue;
                if (name is "bin" or "obj" && Directory.EnumerateFiles(current, "*.csproj").Any()) continue;
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                { yield return Path.GetRelativePath(root, child) + " (unexamined directory link)"; continue; }
                pending.Push(child);
            }
        }
        foreach (var path in bridges.Keys)
            if (!examinedBridges.Contains(path)) yield return path + " (missing restored bridge)";
    }
}
