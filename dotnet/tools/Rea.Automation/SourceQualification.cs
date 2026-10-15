using System.Diagnostics;

namespace Rea.Automation;

public static class SourceQualification
{
    public static async Task RequireCleanCheckoutAsync(string sourceSha)
    {
        var head = (await Git("rev-parse", "--verify", "HEAD")).Trim();
        if (head != sourceSha) throw new InvalidDataException("Selected source SHA is not the checked-out commit");
        if ((await Git("status", "--porcelain", "--untracked-files=normal")).Length != 0)
            throw new InvalidDataException("Source qualification requires a clean committed checkout; local inspection/package structure checks can run on dirty source");
    }

    private static async Task<string> Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Cannot read Git source identity");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            var result = await stdout; var diagnostic = await stderr;
            if (process.ExitCode != 0) throw new InvalidDataException("Cannot qualify Git source: " + diagnostic);
            return result;
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
        }
    }
}
