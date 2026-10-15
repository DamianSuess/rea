using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Rea.Application;
using Rea.Cli;
using Rea.Domain;

namespace Rea.Tests;

internal static class WorkflowTests
{
    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "rea-dotnet-tests-" + Guid.NewGuid().ToString("N"));
        public Workspace() => Directory.CreateDirectory(Root);
        public string Write(string name, byte[] bytes)
        { var path = Path.Combine(Root, name); File.WriteAllBytes(path, bytes); return path; }
        public void Dispose() => Directory.Delete(Root, true);
    }

    public static IEnumerable<Case> Cases()
    {
        yield return new("File workflow retains selected path, bounded bytes and digest", async () =>
        {
            using var workspace = new Workspace(); var bytes = Fixtures.Elf();
            var file = workspace.Write("input with spaces.bin", [..bytes, ..new byte[10000]]);
            var result = Check.Success(await new HeaderInspectionService().InspectAsync(new(file, "x64", 64)));
            Check.Equal(file, result.Source.CallerPath); Check.Equal(Path.GetFullPath(file), result.Source.AbsolutePath);
            Check.Equal(10064L, result.Source.FileBytes); Check.Equal(64, result.Source.RetainedBytes);
            Check.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), result.Source.Sha256);
            Check.True(result.Limitations.Any(value => value.Contains("not examined")));
            Check.Equal(10064L, new FileInfo(file).Length);
        });
        yield return new("File workflow cancellation and missing input are tagged", async () =>
        {
            using var workspace = new Workspace(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var service = new HeaderInspectionService();
            Check.Equal("cancelled", Check.Failure(await service.InspectAsync(new(Path.Combine(workspace.Root, "missing"), "x64"), cancellation.Token)).Code);
            Check.Equal("not-found", Check.Failure(await service.InspectAsync(new(Path.Combine(workspace.Root, "missing"), "x64"))).Code);
            Check.Equal("invalid-input", Check.Failure(await service.InspectAsync(new("bad\0path", "x64"))).Code);
            Check.Equal("invalid-input", Check.Failure(await service.InspectAsync(new("x", "x64", 0))).Code);
            Check.Equal("invalid-input", Check.Failure(await service.InspectAsync(new("x", "x64", FormatHint: "raw-binary"))).Code);
        });
        yield return new("CLI returns parsed metadata with provenance and omits unknown family fields", async () =>
        {
            using var workspace = new Workspace(); var file = workspace.Write("mips.bin", Fixtures.Elf(machine: 8));
            using var output = new StringWriter(); using var error = new StringWriter();
            Check.Equal(0, await CliApplication.RunAsync(["inspect-header", file], output, error));
            using var doc = JsonDocument.Parse(output.ToString()); var value = doc.RootElement.GetProperty("value"); var header = value.GetProperty("header");
            Check.Equal("mips", header.GetProperty("architecture").GetString());
            Check.Equal(64, header.GetProperty("mips").GetProperty("elfClass").GetInt32());
            Check.True(!header.TryGetProperty("riscv", out _)); Check.True(!header.TryGetProperty("powerpc", out _));
            Check.Equal(file, value.GetProperty("source").GetProperty("callerPath").GetString()); Check.Equal("", error.ToString());
        });
        yield return new("CLI preserves explicit null CodeView GUID", async () =>
        {
            using var workspace = new Workspace(); var bytes = Fixtures.Pe(codeView: true);
            Array.Clear(bytes, 540, bytes.Length - 540); var cv = Fixtures.CodeView(false); cv.CopyTo(bytes, 540); Fixtures.U32(bytes, 528, (uint)cv.Length);
            var file = workspace.Write("nb10.exe", bytes); using var output = new StringWriter(); using var error = new StringWriter();
            Check.Equal(0, await CliApplication.RunAsync(["inspect-header", file], output, error));
            using var doc = JsonDocument.Parse(output.ToString());
            Check.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("value").GetProperty("header").GetProperty("codeView").GetProperty("guid").ValueKind);
        });
        yield return new("CLI bounded header does not invent absent data", async () =>
        {
            using var workspace = new Workspace(); var file = workspace.Write("managed.exe", Fixtures.Pe(managed: true));
            using var output = new StringWriter(); using var error = new StringWriter();
            Check.Equal(1, await CliApplication.RunAsync(["inspect-header", file, "--header-bytes", "64"], output, error));
            using var doc = JsonDocument.Parse(output.ToString()); Check.True(!doc.RootElement.GetProperty("ok").GetBoolean());
            Check.Equal("invalid-header", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        });
        yield return new("CLI only interprets COM when caller selects it", async () =>
        {
            using var workspace = new Workspace(); var file = workspace.Write("test.com", [0xc3]);
            using var output = new StringWriter(); using var error = new StringWriter();
            Check.Equal(1, await CliApplication.RunAsync(["inspect-header", file], output, error));
            output.GetStringBuilder().Clear();
            Check.Equal(0, await CliApplication.RunAsync(["inspect-header", file, "--format", "dos-com"], output, error));
            using var doc = JsonDocument.Parse(output.ToString()); Check.Equal("dos-com", doc.RootElement.GetProperty("value").GetProperty("header").GetProperty("format").GetString());
        });
        foreach (var args in new[] {
            new[] { "mcp" }, new[] { "inspect-header" }, new[] { "inspect-header", "x", "--header-bytes", "-1" },
            new[] { "inspect-header", "x", "--unknown", "y" }, new[] { "inspect-header", "x", "--format" },
            new[] { "inspect-header", "x", "--format", "elf" },
            new[] { "inspect-header", "x", "--host-architecture", "sparc" },
            new[] { "inspect-header", "x", "--header-bytes", "64", "--header-bytes", "128" } })
            yield return new($"CLI rejects invalid arguments: {string.Join(' ', args)}", async () =>
            {
                using var output = new StringWriter(); using var error = new StringWriter();
                Check.Equal(2, await CliApplication.RunAsync(args, output, error)); Check.Equal("", output.ToString());
                using var doc = JsonDocument.Parse(error.ToString()); Check.Equal("invalid-input", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
            });
        yield return new("Real CLI subprocess inspects its managed PE without Node", async () =>
        {
            var assembly = typeof(CliApplication).Assembly.Location;
            var runtimeConfig = Path.ChangeExtension(typeof(WorkflowTests).Assembly.Location, "runtimeconfig.json");
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "exec", "--runtimeconfig", runtimeConfig, assembly, "inspect-header", assembly }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("CLI failed to start");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token); var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                Check.Equal(0, process.ExitCode); Check.Equal("", await stderr);
                using var doc = JsonDocument.Parse(await stdout);
                var header = doc.RootElement.GetProperty("value").GetProperty("header");
                Check.Equal("pe", header.GetProperty("format").GetString()); Check.True(header.GetProperty("managed").GetBoolean());
            }
            finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
        });
    }
}
