using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rea.Experiments;

public static class ExperimentCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellation = default)
    {
        try
        {
            if (args.Length == 5 && args[0] == "measure")
            {
                ExperimentOracle.RequireCommand(args[1]);
                if (!int.TryParse(args[4], out var count) || count <= 0) throw new ArgumentException("Iteration count must be a positive integer");
                var path = Path.GetFullPath(args[3]);
                if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Choose a new report path; existing evidence will not be overwritten");
                var cli = Path.Combine(Root(), "dotnet/src/Rea.Cli/bin/Release/net10.0/rea.dll");
                var cliHash = await Hash(cli); var expected = await ExperimentOracle.Capture(args[1], args[2]);
                var runs = new JsonArray();
                var report = new JsonObject
                {
                    ["schema_version"] = 2, ["status"] = "running", ["command"] = args[1], ["caller_input"] = args[2], ["input"] = Path.GetFullPath(args[2]),
                    ["cli_path"] = cli, ["cli_sha256"] = cliHash, ["runs"] = runs, ["model_tokens"] = null,
                    ["measurement"] = new JsonObject { ["runtime"] = RuntimeInformation.FrameworkDescription, ["os"] = RuntimeInformation.OSDescription, ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(), ["counter_scope"] = "direct-static-cli", ["deadline_ms"] = 30000, ["stdout_bytes"] = 64 * 1024 * 1024, ["stderr_bytes"] = 1024 * 1024, ["sample_ms"] = 5 },
                    ["limitations"] = new JsonArray("Filesystem cache uncontrolled; fresh CLI processes.", "Independent pre/post digests do not establish an atomic filesystem snapshot; header identity covers only the retained prefix.", "CPU and sampled peak memory describe the direct CLI only; descendants, client costs, model tokens, hard CPU/memory limits and cleanup of arbitrary process families are unverified.")
                };
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                Exception? failure = null; var retainedBytes = 4096;
                try
                {
                    for (var run = 1; run <= count; run++)
                    {
                        if (await Hash(cli) != cliHash || await ExperimentOracle.Capture(args[1], args[2]) != expected) throw new InvalidDataException("CLI or input changed before measurement");
                        var start = new ProcessStartInfo("dotnet");
                        foreach (var argument in new[] { "exec", cli, args[1], args[2] }) start.ArgumentList.Add(argument);
                        var observed = await ProcessMeasurement.RunAsync(start, MeasurementLimits.Default, cancellation);
                        var coverage = ExperimentOracle.ValidateReply(args[1], args[2], observed, expected);
                        if (await ExperimentOracle.Capture(args[1], args[2]) != expected || await Hash(cli) != cliHash) throw new InvalidDataException("CLI or input changed during measurement");
                        var entry = new JsonObject { ["run"] = run, ["completed"] = true, ["elapsed_ms"] = observed.ElapsedMs, ["cpu_ms"] = observed.CpuMs, ["sampled_peak_working_set_bytes"] = observed.SampledPeakWorkingSetBytes, ["output_bytes"] = observed.Output.Length, ["output_sha256"] = Convert.ToHexStringLower(SHA256.HashData(observed.Output)), ["input_identity"] = ExperimentReport.Identity(expected), ["coverage"] = coverage };
                        retainedBytes = checked(retainedBytes + Encoding.UTF8.GetByteCount(entry.ToJsonString()) + 1024);
                        if (retainedBytes > ExperimentReport.MaximumBytes / 2) throw new InvalidDataException("Experiment report capacity exceeded");
                        runs.Add(entry);
                    }
                    report["status"] = "completed"; ExperimentReport.Validate(report);
                }
                catch (Exception ex)
                {
                    failure = ex; report["status"] = "failed"; report["error"] = ex.Message;
                    if (ex is MeasurementProcessException process) report["direct_process_exit_verified"] = process.DirectProcessExited;
                }
                var serialized = JsonSerializer.SerializeToUtf8Bytes(report, ExperimentReport.JsonOptions);
                if (serialized.Length > ExperimentReport.MaximumBytes)
                {
                    failure ??= new InvalidDataException("Formatted report capacity exceeded; compact partial evidence retained");
                    report["status"] = "failed"; report["error"] = failure.Message;
                    serialized = Encoding.UTF8.GetBytes(report.ToJsonString());
                }
                await file.WriteAsync(serialized); await file.FlushAsync();
                if (failure is not null) throw new InvalidDataException("Experiment failed; retained report: " + path + ". " + failure.Message, failure);
                await output.WriteLineAsync($"Recorded {count} independently checked CLI tasks at {path}"); return 0;
            }
            if (args.Length == 3 && args[0] == "compare")
            { await output.WriteLineAsync(JsonSerializer.Serialize(ExperimentReport.Compare(await ExperimentReport.Read(args[1]), await ExperimentReport.Read(args[2])))); return 0; }
            if (args.Length == 2 && args[0] == "verify")
            {
                var report = await ExperimentReport.Read(args[1]); var command = ExperimentOracle.Field(report, "command").GetValue<string>();
                var current = await ExperimentOracle.Capture(command, ExperimentOracle.Field(report, "input").GetValue<string>());
                if (!JsonNode.DeepEquals(ExperimentReport.Identity(current), report["runs"]![0]!["input_identity"]) || await Hash(ExperimentOracle.Field(report, "cli_path").GetValue<string>()) != report["cli_sha256"]!.GetValue<string>())
                    throw new InvalidDataException("Report no longer matches current input/CLI bytes");
                await output.WriteLineAsync("Report structure, current input identity and CLI bytes verified."); return 0;
            }
            await error.WriteLineAsync("Usage: measure COMMAND INPUT NEW_REPORT ITERATIONS | compare BASELINE CANDIDATE | verify REPORT"); return 2;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or FormatException or OverflowException or OperationCanceledException or MeasurementProcessException or System.ComponentModel.Win32Exception)
        { await error.WriteLineAsync(ex.Message); return 1; }
    }
    private static async Task<string> Hash(string path)
    { await using var stream = File.OpenRead(path); return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream)); }
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rea.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Build the root solution in Release first");
    }
}
