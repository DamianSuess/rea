using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

return await Experiments.RunAsync(args);

internal static class Experiments
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 5 && args[0] == "measure")
            {
                if (args[1] is not ("inspect-header" or "inspect-managed-artifact")) throw new ArgumentException("Only the two current C# analysis commands can be measured");
                var count = int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
                if (count <= 0) throw new ArgumentException("Iteration count must be positive");
                if (File.Exists(args[3]) || Directory.Exists(args[3])) throw new IOException("Choose a new report path; existing evidence will not be overwritten");
                var root = FindRoot();
                var cli = Path.Combine(root, "dotnet/src/Rea.Cli/bin/Release/net10.0/rea.dll");
                if (!File.Exists(cli)) throw new FileNotFoundException("Build Rea.slnx in Release first", cli);
                var reports = new List<JsonObject>();
                for (var run = 0; run < count; run++) reports.Add(await Measure(cli, args[1], args[2], run + 1));
                var report = new JsonObject
                {
                    ["schema_version"] = 1, ["command"] = args[1], ["input"] = Path.GetFullPath(args[2]),
                    ["cli_sha256"] = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(cli))),
                    ["runs"] = new JsonArray(reports.Select(item => (JsonNode)item).ToArray()), ["model_tokens"] = null,
                    ["limitations"] = new JsonArray("Fresh CLI processes; filesystem cache state is uncontrolled.", "Counters cover the static CLI, which starts no provider children. Measurement-client CPU and memory are excluded.", "Peak working set is sampled, not guaranteed exact; JSON bytes are not model tokens.")
                };
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);
                await using var output = new FileStream(args[3], FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await JsonSerializer.SerializeAsync(output, report, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine($"Recorded {count} completed CLI tasks at {args[3]}"); return 0;
            }
            if (args.Length == 3 && args[0] == "compare")
            {
                var left = JsonNode.Parse(await File.ReadAllTextAsync(args[1]))!; var right = JsonNode.Parse(await File.ReadAllTextAsync(args[2]))!;
                if (!JsonNode.DeepEquals(left["command"], right["command"]) || !JsonNode.DeepEquals(left["input"], right["input"]))
                    throw new InvalidDataException("Comparison commands/inputs differ");
                var a = left["runs"]!.AsArray(); var b = right["runs"]!.AsArray();
                if (a.Count == 0 || b.Count == 0 || a.Concat(b).Any(run => run?["completed"]?.GetValue<bool>() != true))
                    throw new InvalidDataException("Only completed tasks can be compared");
                var identity = a[0]!["input_identity"] ?? throw new InvalidDataException("Observed input identity missing");
                if (a.Concat(b).Any(run => !JsonNode.DeepEquals(run!["input_identity"], identity))) throw new InvalidDataException("Observed input identities differ");
                Console.WriteLine(JsonSerializer.Serialize(new { baselineMeanMs = a.Average(run => run!["elapsed_ms"]!.GetValue<double>()), candidateMeanMs = b.Average(run => run!["elapsed_ms"]!.GetValue<double>()), modelTokens = (long?)null, note = "Observed CLI timing comparison only; token, cold-cache and whole-client costs remain unmeasured." })); return 0;
            }
            Console.Error.WriteLine("Usage: measure COMMAND INPUT NEW_REPORT_JSON ITERATIONS | compare BASELINE_JSON CANDIDATE_JSON"); return 2;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidDataException or JsonException or InvalidOperationException or FormatException or OperationCanceledException or System.ComponentModel.Win32Exception)
        { Console.Error.WriteLine(ex.Message); return 1; }
    }

    private static async Task<JsonObject> Measure(string cli, string command, string input, int run)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var value in new[] { "exec", cli, command, input }) info.ArgumentList.Add(value);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var timer = Stopwatch.StartNew(); using var process = Process.Start(info) ?? throw new InvalidOperationException("CLI failed to start");
        Task<byte[]>? stdout = null; Task<byte[]>? stderr = null;
        try
        {
            stdout = BoundedRead(process.StandardOutput.BaseStream, 64 * 1024 * 1024, timeout.Token);
            stderr = BoundedRead(process.StandardError.BaseStream, 1024 * 1024, timeout.Token);
            long peak = 0; double? cpu = null;
            while (!process.HasExited)
            {
                process.Refresh();
                try { peak = Math.Max(peak, process.PeakWorkingSet64); cpu = process.TotalProcessorTime.TotalMilliseconds; }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException) { }
                if (stdout.IsFaulted) await stdout;
                if (stderr.IsFaulted) await stderr;
                await Task.Delay(5, timeout.Token);
            }
            await process.WaitForExitAsync(timeout.Token); timer.Stop();
            try { cpu = process.TotalProcessorTime.TotalMilliseconds; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException) { }
            var bytes = await stdout; var diagnostics = await stderr;
            if (process.ExitCode != 0) throw new InvalidDataException($"CLI task failed ({process.ExitCode}): {Encoding.UTF8.GetString(diagnostics)} {Encoding.UTF8.GetString(bytes)}");
            var reply = JsonNode.Parse(bytes) ?? throw new InvalidDataException("CLI omitted JSON");
            if (reply["ok"]?.GetValue<bool>() != true) throw new InvalidDataException("CLI did not report success");
            var value = reply["value"]!;
            var identity = command == "inspect-header" ? new JsonObject
            { ["sha256"] = value["source"]!["sha256"]!.GetValue<string>(), ["bytes"] = value["source"]!["fileBytes"]!.GetValue<long>(), ["digest_scope"] = "retained-prefix" }
            : new JsonObject { ["sha256"] = value["artifact"]!["sha256"]!.GetValue<string>(), ["bytes"] = value["artifact"]!["byte_length"]!.GetValue<long>(), ["digest_scope"] = "full-snapshot" };
            return new JsonObject { ["run"] = run, ["completed"] = true, ["elapsed_ms"] = timer.Elapsed.TotalMilliseconds, ["cpu_ms"] = cpu, ["sampled_peak_working_set_bytes"] = peak > 0 ? JsonValue.Create(peak) : null, ["output_bytes"] = bytes.Length, ["output_sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)), ["input_identity"] = identity, ["coverage"] = value["coverage"]?.DeepClone() };
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            foreach (var task in new[] { stdout, stderr })
                if (task is not null)
                    try { await task; } catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    private static async Task<byte[]> BoundedRead(Stream stream, int limit, CancellationToken cancellation)
    {
        using var output = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellation); if (count == 0) return output.ToArray();
            if (output.Length + count > limit) throw new InvalidDataException("CLI output exceeded measurement capacity");
            output.Write(buffer, 0, count);
        }
    }
    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rea.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Root Rea.slnx not found");
    }
}
