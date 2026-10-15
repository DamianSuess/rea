using InstallerCommand = Rea.Installer.Installer;

namespace Rea.Tests;

internal static class InstallerTests
{
    public static IEnumerable<Case> Cases()
    {
        yield return new Case("Installer dry run and declined plan make no destination changes", async () =>
        {
            var destination = Path.Combine(Path.GetTempPath(), "rea-installer-" + Guid.NewGuid().ToString("N"));
            var output = new StringWriter(); var error = new StringWriter();
            Check.Equal(0, await InstallerCommand.RunAsync(["--tool-path", destination, "--dry-run"], new StringReader(""), output, error));
            Check.True(!Directory.Exists(destination));
            Check.True(output.ToString().Contains("REA install plan", StringComparison.Ordinal));
            Check.Equal(0, await InstallerCommand.RunAsync(["--tool-path", destination], new StringReader("no\n"), output, error));
            Check.True(!Directory.Exists(destination));
            Check.Equal(1, await InstallerCommand.RunAsync(["--tool-path", destination, "--no-prompt"], new StringReader("yes\n"), output, error));
            Check.True(!Directory.Exists(destination));
        });
        yield return new Case("Installer rejects unsupported options and missing packages before changes", async () =>
        {
            foreach (var args in new[] { new[] { "--version", "0.1.0" }, new[] { "--package" }, new[] { "--yes", "--yes" }, new[] { "--package", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nupkg"), "--yes" } })
                Check.Equal(1, await InstallerCommand.RunAsync(args, new StringReader(""), new StringWriter(), new StringWriter()));
        });
    }
}
