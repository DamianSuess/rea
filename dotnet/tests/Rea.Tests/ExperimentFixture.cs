namespace Rea.Tests;

internal static class ExperimentFixture
{
    public static async Task<int> Run(string[] args)
    {
        switch (args[0])
        {
            case "hold" when args.Length == 2:
                await using (var held = new FileStream(args[1], FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                { await held.WriteAsync(new byte[] { 1 }); await held.FlushAsync(); await Task.Delay(TimeSpan.FromSeconds(30)); }
                return 0;
            case "stdout" or "stderr":
                var stream = args[0] == "stdout" ? Console.OpenStandardOutput() : Console.OpenStandardError();
                await stream.WriteAsync(new byte[16384]); await stream.FlushAsync(); await Task.Delay(TimeSpan.FromSeconds(30)); return 0;
            case "fail": await Console.Error.WriteLineAsync("controlled C# child failure"); return 13;
            default: return 64;
        }
    }
}
