using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rea.Experiments;

public sealed record InputIdentity(string Sha256, long Bytes, string DigestScope, long RetainedBytes);
public static class ExperimentOracle
{
    public static async Task<InputIdentity> Capture(string command, string path)
    {
        RequireCommand(command);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        var length = input.Length;
        if (command == "inspect-managed-artifact" && length > 64 * 1024 * 1024) throw new InvalidDataException("Input exceeds the measured command's default 64 MiB budget");
        var retained = command == "inspect-header" ? Math.Min(length, 65536) : length;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[8192]; long read = 0;
        while (read < retained)
        {
            var count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, retained - read)));
            if (count == 0) throw new InvalidDataException("Input changed during independent digest capture");
            hash.AppendData(buffer, 0, count); read += count;
        }
        if (input.Length != length) throw new InvalidDataException("Input length changed during capture");
        return new(Convert.ToHexStringLower(hash.GetHashAndReset()), length, command == "inspect-header" ? "retained-prefix" : "full-snapshot", retained);
    }
    public static JsonNode? ValidateReply(string command, string callerPath, ProcessObservation observation, InputIdentity expected)
    {
        RequireCommand(command);
        if (observation.ExitCode != 0) throw new InvalidDataException("CLI task failed with exit " + observation.ExitCode);
        var reply = JsonNode.Parse(observation.Output) ?? throw new InvalidDataException("CLI omitted JSON");
        if (Field(reply, "ok").GetValue<bool>() != true) throw new InvalidDataException("CLI did not report success");
        var value = Field(reply, "value"); JsonNode identity;
        if (command == "inspect-header")
        {
            identity = Field(value, "source");
            if (Field(identity, "callerPath").GetValue<string>() != callerPath || Field(identity, "offset").GetValue<long>() != 0 || Field(identity, "retainedBytes").GetValue<long>() != expected.RetainedBytes)
                throw new InvalidDataException("Header result changed caller input or digest extent");
            _ = Field(Field(value, "header"), "format").GetValue<string>();
        }
        else
        {
            identity = Field(value, "artifact");
            if (Field(identity, "path").GetValue<string>() != callerPath) throw new InvalidDataException("Managed result changed caller input");
            var state = Field(Field(value, "coverage"), "state").GetValue<string>();
            if (state is not ("complete" or "partial")) throw new InvalidDataException("Unknown coverage state");
            if (Field(Field(value, "coverage"), "issues") is not JsonArray) throw new InvalidDataException("Coverage issues must be an array");
        }
        if (Field(identity, "sha256").GetValue<string>() != expected.Sha256 || Field(identity, command == "inspect-header" ? "fileBytes" : "byte_length").GetValue<long>() != expected.Bytes)
            throw new InvalidDataException("CLI identity differs from independently captured input bytes");
        return command == "inspect-header" ? null : Field(value, "coverage").DeepClone();
    }
    public static void RequireCommand(string command)
    { if (command is not ("inspect-header" or "inspect-managed-artifact")) throw new ArgumentException("Only the two current C# analysis commands can be measured"); }
    public static JsonNode Field(JsonNode? value, string name) => value is JsonObject obj && obj.TryGetPropertyValue(name, out var result) && result is not null
        ? result : throw new InvalidDataException("Missing required field: " + name);
}
