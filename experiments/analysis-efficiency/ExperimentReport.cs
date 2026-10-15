using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Rea.Experiments.ExperimentOracle;

namespace Rea.Experiments;

public static class ExperimentReport
{
    public const int MaximumBytes = 16 * 1024 * 1024;
    public static JsonSerializerOptions JsonOptions { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };
    public static JsonNode Identity(InputIdentity value) => JsonSerializer.SerializeToNode(value, JsonOptions)!;
    public static async Task<JsonNode> Read(string path)
    {
        if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("Experiment report exceeds the 16 MiB budget");
        var report = JsonNode.Parse(await File.ReadAllTextAsync(path)) ?? throw new InvalidDataException("Empty experiment report");
        Validate(report); return report;
    }
    public static void Validate(JsonNode report)
    {
        if (Field(report, "schema_version").GetValue<int>() != 2) throw new InvalidDataException("Expected experiment schema 2; old measurements must be regenerated");
        var command = Field(report, "command").GetValue<string>(); RequireCommand(command);
        if (Field(report, "status").GetValue<string>() != "completed") throw new InvalidDataException("Only completed experiment reports can be verified or compared");
        foreach (var key in new[] { "input", "caller_input", "cli_path" })
            if (string.IsNullOrWhiteSpace(Field(report, key).GetValue<string>())) throw new InvalidDataException("Missing report path: " + key);
        if (!Path.IsPathFullyQualified(Field(report, "input").GetValue<string>()) || !Path.IsPathFullyQualified(Field(report, "cli_path").GetValue<string>())) throw new InvalidDataException("Report lookup paths must be absolute");
        Digest(Field(report, "cli_sha256"));
        var measurement = Field(report, "measurement");
        foreach (var key in new[] { "runtime", "os", "architecture", "counter_scope" })
            if (string.IsNullOrWhiteSpace(Field(measurement, key).GetValue<string>())) throw new InvalidDataException("Missing measurement context");
        foreach (var key in new[] { "deadline_ms", "stdout_bytes", "stderr_bytes", "sample_ms" }) Number(Field(measurement, key), true);
        if (report is not JsonObject root || !root.ContainsKey("model_tokens") || report["model_tokens"] is not null) throw new InvalidDataException("Unmeasured model tokens must remain explicit null");
        var runs = Field(report, "runs") as JsonArray ?? throw new InvalidDataException("Runs must be an array");
        if (runs.Count == 0) throw new InvalidDataException("No measured tasks");
        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i] ?? throw new InvalidDataException("Null run");
            if (Field(run, "run").GetValue<int>() != i + 1 || !Field(run, "completed").GetValue<bool>()) throw new InvalidDataException("Missing, duplicate or incomplete run");
            Number(Field(run, "elapsed_ms"), true); Number(Field(run, "output_bytes"), true); Digest(Field(run, "output_sha256"));
            foreach (var key in new[] { "cpu_ms", "sampled_peak_working_set_bytes" })
            {
                if (run is not JsonObject obj || !obj.ContainsKey(key)) throw new InvalidDataException("Counter omission must be explicit null");
                if (run[key] is { } counter) Number(counter, key != "cpu_ms");
            }
            var identity = Field(run, "input_identity"); Digest(Field(identity, "sha256"));
            var bytes = Field(identity, "bytes").GetValue<long>(); var retained = Field(identity, "retained_bytes").GetValue<long>();
            var scope = Field(identity, "digest_scope").GetValue<string>();
            if (bytes < 0 || retained < 0 || retained > bytes || (command == "inspect-header" ? scope != "retained-prefix" || retained != Math.Min(bytes, 65536) : scope != "full-snapshot" || retained != bytes))
                throw new InvalidDataException("Invalid input digest scope or extent");
            if (!JsonNode.DeepEquals(identity, Field(runs[0], "input_identity"))) throw new InvalidDataException("Input identity changed between runs");
            if (command == "inspect-managed-artifact")
            {
                var coverage = Field(run, "coverage");
                if (Field(coverage, "state").GetValue<string>() is not ("complete" or "partial") || Field(coverage, "issues") is not JsonArray)
                    throw new InvalidDataException("Invalid managed coverage");
            }
            else if (run is not JsonObject header || !header.ContainsKey("coverage") || run["coverage"] is not null) throw new InvalidDataException("Header-only coverage must remain explicit null");
        }
    }
    public static object Compare(JsonNode left, JsonNode right)
    {
        Validate(left); Validate(right);
        foreach (var key in new[] { "command", "input", "caller_input", "measurement" })
            if (!JsonNode.DeepEquals(left[key], right[key])) throw new InvalidDataException("Comparison context differs: " + key);
        var a = Field(left, "runs").AsArray(); var b = Field(right, "runs").AsArray(); var first = a[0]!;
        foreach (var run in a.Concat(b))
            if (!JsonNode.DeepEquals(run!["input_identity"], first["input_identity"]) || !JsonNode.DeepEquals(run["coverage"], first["coverage"]))
                throw new InvalidDataException("Comparison input identity or coverage differs");
        return new { baseline_mean_ms = a.Average(run => Field(run, "elapsed_ms").GetValue<double>()), candidate_mean_ms = b.Average(run => Field(run, "elapsed_ms").GetValue<double>()), model_tokens = (long?)null, limitations = "Recorded CLI timing only; no speedup inference, token measurement, cold-cache control or whole-client cost measurement." };
    }
    private static void Number(JsonNode value, bool positive)
    {
        if (!double.TryParse(value.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || number < 0 || positive && number == 0)
            throw new InvalidDataException("Invalid measurement counter");
    }
    private static void Digest(JsonNode value)
    { if (!Regex.IsMatch(value.GetValue<string>(), @"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new InvalidDataException("Invalid SHA-256 identity"); }
}
