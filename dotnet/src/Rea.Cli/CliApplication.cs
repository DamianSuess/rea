using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rea.Application;
using Rea.Domain;

namespace Rea.Cli;

public static class CliApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
    private const string Help = """
        REA .NET 10 migration preview
        Usage: rea inspect-header PATH [--host-architecture HOST] [--header-bytes N] [--format dos-com]
        HOST: x64, arm64, ia32, arm. Defaults to the current process architecture.
        N: positive retained-prefix byte budget (default 65536).
        Static local reads only. JSON includes retained-prefix provenance and limitations.
        This preview does not yet implement the complete REA CLI or MCP server.
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || (args.Length == 1 && args[0] is "--help" or "-h"))
        { await output.WriteLineAsync(Help); return 0; }
        if (args.Length == 1 && args[0] == "--version")
        { await output.WriteLineAsync("rea-dotnet 0.1.0-preview"); return 0; }
        if (args.Length < 2 || args[0] != "inspect-header")
            return await UsageError("Expected inspect-header PATH", error);
        var host = RuntimeInformation.ProcessArchitecture switch
        { Architecture.X64 => "x64", Architecture.X86 => "ia32", Architecture.Arm => "arm", Architecture.Arm64 => "arm64", _ => "unknown" };
        var bytes = 65536;
        string? format = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 2; i < args.Length; i += 2)
        {
            var option = args[i];
            if (option is not ("--host-architecture" or "--header-bytes" or "--format")) return await UsageError($"Unknown option: {option}", error);
            if (!seen.Add(option)) return await UsageError($"Duplicate option: {option}", error);
            if (i + 1 >= args.Length) return await UsageError($"Missing value for {option}", error);
            var value = args[i + 1];
            switch (option)
            {
                case "--host-architecture":
                    if (value is not ("x64" or "arm64" or "ia32" or "arm")) return await UsageError("Unsupported host architecture", error);
                    host = value; break;
                case "--header-bytes":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out bytes) || bytes <= 0 || bytes > Array.MaxLength)
                        return await UsageError("Header bytes must fit a positive CLR byte-array length", error);
                    break;
                case "--format":
                    if (value != "dos-com") return await UsageError("Supported explicit format: dos-com", error);
                    format = value; break;
            }
        }
        var result = await new HeaderInspectionService().InspectAsync(new(args[1], host, bytes, format), cancellationToken);
        if (result is Result<HeaderInspection, InspectionError>.Success success)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(new { ok = true, value = success.Value }, JsonOptions));
            return 0;
        }
        var failure = ((Result<HeaderInspection, InspectionError>.Failure)result).Error;
        await output.WriteLineAsync(JsonSerializer.Serialize(new { ok = false, error = failure }, JsonOptions));
        return failure.Code == "cancelled" ? 130 : 1;
    }

    private static async Task<int> UsageError(string message, TextWriter error)
    {
        await error.WriteLineAsync(JsonSerializer.Serialize(new { ok = false, error = new InspectionError("invalid-input", message) }, JsonOptions));
        return 2;
    }
}
