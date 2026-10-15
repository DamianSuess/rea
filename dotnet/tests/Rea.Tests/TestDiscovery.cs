using System.Reflection;

namespace Rea.Tests;

internal static class TestDiscovery
{
    public static Case[] Discover(string? packageDirectory = null)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Rea.slnx"))) root = root.Parent;
        if (root is null) throw new InvalidDataException("Cannot locate test source for discovery validation");
        var sources = Directory.GetFiles(Path.Combine(root.FullName, "dotnet/tests/Rea.Tests"), "*Tests.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(Path.Combine(root.FullName, "dotnet/tests/Rea.Tests"), path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);
        var types = typeof(TestDiscovery).Assembly.GetTypes().Where(type => (type.Namespace == "Rea.Tests" || type.Namespace?.StartsWith("Rea.Tests.", StringComparison.Ordinal) == true) && type.Name.EndsWith("Tests", StringComparison.Ordinal) && !type.IsNested).ToArray();
        ValidateInventory(sources, types.Select(type => type.Name));
        var result = new List<Case>();
        foreach (var type in types.OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(method => method.Name == "Cases").ToArray();
            if (methods.Length != 1 || methods[0].ReturnType != typeof(IEnumerable<Case>)) throw new InvalidDataException(type.Name + " must declare exactly one public static IEnumerable<Case> Cases method");
            var method = methods[0]; var parameters = method.GetParameters();
            var packaged = type == typeof(PackageTests);
            if (packaged ? parameters.Length != 1 || parameters[0].ParameterType != typeof(string) : parameters.Length != 0)
                throw new InvalidDataException("Unexpected suite signature: " + type.Name);
            if (packaged != (packageDirectory is not null)) continue;
            var cases = ((IEnumerable<Case>)method.Invoke(null, packaged ? [packageDirectory] : null)!).ToArray();
            if (cases.Length == 0) throw new InvalidDataException("Empty suite: " + type.Name);
            result.AddRange(cases);
        }
        ValidateCaseNames(result.Select(test => test.Name));
        return result.ToArray();
    }

    internal static void ValidateInventory(IEnumerable<string?> sources, IEnumerable<string> compiled)
    {
        var expected = sources.ToHashSet(StringComparer.Ordinal); var actual = compiled.ToArray();
        if (expected.Count == 0 || actual.Distinct(StringComparer.Ordinal).Count() != actual.Length || !expected.SetEquals(actual))
            throw new InvalidDataException("Test source/compiled discovery mismatch (missing, unexpected or duplicate suite)");
    }
    internal static void ValidateCaseNames(IEnumerable<string> names)
    {
        var values = names.ToArray();
        if (values.Length == 0 || values.Any(string.IsNullOrWhiteSpace) || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidDataException("Missing or duplicate test case names");
    }
}
