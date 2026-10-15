using System.Diagnostics;
using Rea.Tests;

var tests = DomainTests.Cases().Concat(WorkflowTests.Cases()).ToArray();
if (tests.Length == 0) throw new InvalidOperationException("No tests discovered");
var timer = Stopwatch.StartNew();
var failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {test.Name}\n{ex}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed in {timer.Elapsed.TotalSeconds:F2}s");
return failures == 0 ? 0 : 1;
