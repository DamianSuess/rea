using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rea.Experiments;

namespace Rea.Tests;

internal static class ExperimentTests
{
    public static IEnumerable<Case> Cases()
    {
        foreach (var command in new[] { "inspect-header", "inspect-managed-artifact" })
            yield return new("Experiment public workflow independently verifies " + command, async () =>
            {
                using var fixture = new Fixture(); var input = fixture.Input(); var report = Path.Combine(fixture.Path, "report.json");
                Check.Equal(0, await Invoke("measure", command, input, report, "2"));
                Check.Equal(0, await Invoke("verify", report));
                Check.Equal(0, await Invoke("compare", report, report));
                var original = SHA256.HashData(File.ReadAllBytes(report));
                Check.Equal(1, await Invoke("measure", command, input, report, "1"));
                Check.True(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(report))));
                var document = await ExperimentReport.Read(report); Check.Equal(input, document["caller_input"]!.GetValue<string>());
                foreach (var mutation in new Action<JsonNode>[]
                {
                    node => node["schema_version"] = 1,
                    node => node["runs"] = new JsonArray(),
                    node => node["runs"]![0]!["elapsed_ms"] = -1,
                    node => node["runs"]![0]!["completed"] = false,
                    node => node["runs"]![1]!["run"] = 1,
                    node => node["runs"]![0]!["output_sha256"] = "invalid",
                    node => node["runs"]![0]!.AsObject().Remove("cpu_ms"),
                    node => node["runs"]![0]!["input_identity"]!["retained_bytes"] = -1,
                    node => node["model_tokens"] = 10,
                    node => node.AsObject().Remove("model_tokens"),
                    node => node["runs"]![0]!["input_identity"]!["digest_scope"] = command == "inspect-header" ? "full-snapshot" : "retained-prefix"
                })
                { var changed = document.DeepClone(); mutation(changed); Reject(() => ExperimentReport.Validate(changed)); }
                foreach (var mutation in new Action<JsonNode>[]
                {
                    node => node["input"] = Path.Combine(fixture.Path, "other.dll"),
                    node => node["caller_input"] = "different caller spelling.dll",
                    node => node["measurement"]!["sample_ms"] = 100,
                    node => { foreach (var run in node["runs"]!.AsArray()) run!["input_identity"]!["sha256"] = new string('0', 64); }
                })
                { var changed = document.DeepClone(); mutation(changed); Reject(() => ExperimentReport.Compare(document, changed)); }
                if (command == "inspect-managed-artifact")
                {
                    var changed = document.DeepClone(); changed["runs"]![0]!["coverage"]!["state"] = "partial";
                    Reject(() => ExperimentReport.Compare(document, changed));
                }
                await using (var stream = new FileStream(input, FileMode.Append)) await stream.WriteAsync(new byte[] { 1 });
                Check.Equal(1, await Invoke("verify", report));
            });
        yield return new("Experiment prefix identity never implies whole-file identity", async () =>
        {
            using var fixture = new Fixture(); var input = fixture.Input();
            var bytes = new byte[100000]; File.ReadAllBytes(input).CopyTo(bytes, 0); await File.WriteAllBytesAsync(input, bytes);
            var prefix = await ExperimentOracle.Capture("inspect-header", input); var full = await ExperimentOracle.Capture("inspect-managed-artifact", input);
            Check.Equal(65536L, prefix.RetainedBytes); Check.Equal(100000L, full.RetainedBytes);
            Check.True(prefix.Sha256 != full.Sha256);
            Check.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(0, 65536))), prefix.Sha256);
            Check.Equal(0, await Invoke("measure", "inspect-header", input, Path.Combine(fixture.Path, "prefix.json"), "1"));
            bytes[^1] = 1; await File.WriteAllBytesAsync(input, bytes);
            Check.Equal(prefix, await ExperimentOracle.Capture("inspect-header", input));
            Check.True(full != await ExperimentOracle.Capture("inspect-managed-artifact", input));
        });
        yield return new("Experiment failed CLI tasks retain failure evidence and cannot compare green", async () =>
        {
            using var fixture = new Fixture(); var input = Path.Combine(fixture.Path, "invalid.bin"); await File.WriteAllBytesAsync(input, new byte[8]);
            var report = Path.Combine(fixture.Path, "failed.json");
            Check.Equal(1, await Invoke("measure", "inspect-header", input, report, "1"));
            Check.Equal("failed", JsonNode.Parse(await File.ReadAllTextAsync(report))!["status"]!.GetValue<string>());
            Check.Equal(1, await Invoke("verify", report)); Check.Equal(1, await Invoke("compare", report, report));
            Check.Equal(1, await Invoke("measure", "unavailable-provider", input, Path.Combine(fixture.Path, "other.json"), "1"));
        });
        yield return new("Experiment oracle rejects forged digest scope caller identity and malformed success", async () =>
        {
            using var fixture = new Fixture(); var input = fixture.Input(); var expected = await ExperimentOracle.Capture("inspect-header", input);
            var reply = new JsonObject { ["ok"] = true, ["value"] = new JsonObject { ["source"] = new JsonObject { ["callerPath"] = input, ["offset"] = 0L, ["retainedBytes"] = expected.RetainedBytes, ["fileBytes"] = expected.Bytes, ["sha256"] = expected.Sha256 }, ["header"] = new JsonObject { ["format"] = "pe" } } };
            ProcessObservation Observation(JsonNode json) => new(0, Encoding.UTF8.GetBytes(json.ToJsonString()), [], 1, null, null);
            _ = ExperimentOracle.ValidateReply("inspect-header", input, Observation(reply), expected);
            foreach (var key in new[] { "callerPath", "sha256", "retainedBytes" })
            {
                var changed = reply.DeepClone(); changed["value"]!["source"]![key] = key == "retainedBytes" ? JsonValue.Create(-1) : JsonValue.Create("wrong");
                Reject(() => ExperimentOracle.ValidateReply("inspect-header", input, Observation(changed), expected));
            }
            Reject(() => ExperimentOracle.ValidateReply("inspect-header", input, Observation(new JsonObject()), expected));
        });
        yield return new("Experiment measurement preserves nonzero process exit and rejects it as success", async () =>
        {
            var observed = await ProcessMeasurement.RunAsync(Child("fail"), MeasurementLimits.Default);
            Check.Equal(13, observed.ExitCode); Check.True(Encoding.UTF8.GetString(observed.Error).Contains("controlled", StringComparison.Ordinal));
            Reject(() => ExperimentOracle.ValidateReply("inspect-header", "x", observed, new("", 0, "retained-prefix", 0)));
        });
        foreach (var channel in new[] { "stdout", "stderr" })
            yield return new("Experiment output budget stops owned C# child: " + channel, async () =>
            {
                try { await ProcessMeasurement.RunAsync(Child(channel), new(TimeSpan.FromSeconds(5), 128, 128)); throw new InvalidOperationException("Flood child unexpectedly passed"); }
                catch (MeasurementProcessException ex) { Check.True(ex.DirectProcessExited); Check.True(ex.RetainedProcess is null); Check.True(ex.Message.Contains("capacity", StringComparison.Ordinal)); }
            });
        yield return new("Experiment deadline terminates its owned C# child", async () =>
        {
            using var fixture = new Fixture();
            try { await ProcessMeasurement.RunAsync(Child("hold", Path.Combine(fixture.Path, "deadline.lock")), new(TimeSpan.FromMilliseconds(250), 1024, 1024)); throw new InvalidOperationException("Sleeping child unexpectedly passed"); }
            catch (MeasurementProcessException ex) { Check.True(ex.DirectProcessExited); Check.True(ex.Message.Contains("deadline", StringComparison.Ordinal)); }
        });
        yield return new("Experiment cancellation releases the owned child's exclusive file lease", async () =>
        {
            using var fixture = new Fixture(); using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var path = Path.Combine(fixture.Path, "held.lock"); var task = ProcessMeasurement.RunAsync(Child("hold", path), MeasurementLimits.Default, cancellation.Token);
            try
            {
                var watch = Stopwatch.StartNew();
                while (!File.Exists(path) && !task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(8)) await Task.Delay(10);
                Check.True(File.Exists(path), "Controlled child never acquired its file lease"); cancellation.Cancel();
                try { await task; throw new InvalidOperationException("Cancelled child unexpectedly passed"); }
                catch (MeasurementProcessException ex) { Check.True(ex.DirectProcessExited); Check.True(ex.Message.Contains("cancelled", StringComparison.Ordinal)); }
                using var released = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            finally { cancellation.Cancel(); try { await task; } catch (MeasurementProcessException) { } }
        });
    }
    private static async Task<int> Invoke(params string[] args) => await ExperimentCommand.RunAsync(args, new StringWriter(), new StringWriter());
    private static void Reject(Action action)
    { var rejected = false; try { action(); } catch (Exception ex) when (ex is InvalidDataException or JsonException or InvalidOperationException) { rejected = true; } Check.True(rejected, "Experiment checker accepted an intentional invalid result"); }
    private static ProcessStartInfo Child(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet");
        foreach (var arg in new[] { "exec", typeof(ExperimentTests).Assembly.Location, "--experiment-fixture" }.Concat(args)) start.ArgumentList.Add(arg);
        return start;
    }
    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rea-experiment-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Path);
        public string Input()
        { var path = System.IO.Path.Combine(Path, "fixture with spaces.dll"); File.Copy(System.IO.Path.Combine(AppContext.BaseDirectory, "Rea.ManagedFixture.dll"), path); return path; }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
