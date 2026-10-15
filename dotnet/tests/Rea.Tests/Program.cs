using System.Diagnostics;
using Rea.Tests;

string? reportPath = null;
if (args.Length == 2 && args[0] == "--report") { reportPath = args[1]; args = []; }
var selected = args.Length == 0
    ? TestDiscovery.Discover()
    : args.Length == 2 && args[0] == "--verify-packages"
        ? TestDiscovery.Discover(args[1])
        : throw new ArgumentException("Usage: Rea.Tests [--verify-packages DIRECTORY]");
var tests = selected.ToArray();
if (tests.Length == 0) throw new InvalidOperationException("No tests discovered");
var timer = Stopwatch.StartNew();
var failures = 0;
var results = new List<object>();
foreach (var test in tests)
{
    var caseTimer = Stopwatch.StartNew(); string? error = null;
    try { await test.Run(); }
    catch (Exception ex) { failures++; error = ex.ToString(); Console.Error.WriteLine($"FAIL {test.Name}\n{ex}"); }
    results.Add(new { name = test.Name, passed = error is null, elapsed_ms = caseTimer.Elapsed.TotalMilliseconds, error });
}
if (reportPath is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
    await File.WriteAllTextAsync(reportPath, System.Text.Json.JsonSerializer.Serialize(new { failures, cases = results }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed in {timer.Elapsed.TotalSeconds:F2}s");
return failures == 0 ? 0 : 1;
