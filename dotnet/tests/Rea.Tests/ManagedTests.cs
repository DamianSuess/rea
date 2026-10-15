using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Rea.Application;
using Rea.Cli;
using Rea.Domain;
using Rea.Domain.Managed;
using Rea.Managed;

namespace Rea.Tests;

internal static class ManagedTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Rea.ManagedFixture.dll");
    private static ManagedArtifactIdentity Identity(byte[] bytes) => new("caller-selected.dll", Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length);
    private static ManagedArtifactInspection Inspect(byte[] bytes) => Check.Success(new ManagedArtifactInspector().Inspect(ManagedArtifactSnapshot.Create("caller-selected.dll", bytes), default));

    public static IEnumerable<Case> Cases()
    {
        yield return Case.Sync("Managed inventory reads a real compiled assembly without triggering its initializer", () =>
        {
            var bytes = File.ReadAllBytes(FixturePath); var result = Inspect(bytes);
            Check.Equal("managed", result.Classification.Status); Check.Equal("modern-dotnet", result.Classification.RuntimeFamily);
            Check.Equal("complete", result.Coverage.State); Check.Equal("anycpu", result.Classification.ManagedArchitecture);
            Check.Equal("Rea.ManagedFixture", result.Assembly!.Name); Check.Equal("1.2.3.4", result.Assembly.Version);
            Check.Equal("Rea.ManagedFixture.dll", result.Module!.Name); Check.True(Guid.TryParse(result.Module.Mvid, out _));
            Check.True(result.Module.EncId is null); Check.True(result.TargetFrameworks.Contains(".NETCoreApp,Version=v10.0"));
            Check.True(result.References.Any(value => value.Name == "System.Runtime" && value.PublicKeyOrToken.Token == "b03f5f7f11d50a3a"));
            Check.True(result.Metadata.StreamNames.Contains("#Strings")); Check.True(result.Metadata.TableRowCounts["MethodDef"] > 0);
            Check.Equal("caller-selected.dll", result.Artifact.Path); Check.Equal(Identity(bytes).Sha256, result.Artifact.Sha256);
            Check.Equal("rea.fixture.payload", result.Resources.Single().Name);
            using var pe = new PEReader(new MemoryStream(bytes, false)); var reader = pe.GetMetadataReader();
            Check.Equal((long)pe.PEHeaders.MetadataStartOffset + reader.GetTableMetadataOffset(TableIndex.Module), result.Module.RowOffset);
        });
        yield return Case.Sync("Embedded resource bytes have an exact digest and range", () =>
        {
            var bytes = File.ReadAllBytes(FixturePath); var resource = Inspect(bytes).Resources.Single();
            using var pe = new PEReader(new MemoryStream(bytes, false));
            var data = pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress).GetContent().AsSpan();
            var offset = (int)resource.DeclaredOffset; var length = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
            Check.True(resource.Embedded); Check.Equal((int?)length, resource.DataLength);
            Check.Equal(Convert.ToHexStringLower(SHA256.HashData(data.Slice(offset + 4, length))), resource.DataSha256);
            Check.True(resource.ImplementationToken is null);
        });
        yield return Case.Sync("Malformed resource retains its declaration and the other inventory", () =>
        {
            var bytes = File.ReadAllBytes(FixturePath); int offset;
            using (var pe = new PEReader(new MemoryStream(bytes, false)))
            { var cli = pe.PEHeaders.CorHeader!; offset = Map(pe, cli.ResourcesDirectory.RelativeVirtualAddress); }
            Fixtures.U32(bytes, offset, uint.MaxValue);
            var result = Inspect(bytes); Check.Equal("partial", result.Coverage.State);
            Check.Equal("rea.fixture.payload", result.Resources.Single().Name); Check.True(result.Resources.Single().DataLength is null);
            Check.True(result.Coverage.Issues.Any(value => value.Code == "invalid-resource"));
            Check.Equal("Rea.ManagedFixture", result.Assembly!.Name);
        });
        yield return Case.Sync("Malformed module heap reference does not erase assembly facts", () =>
        {
            var bytes = File.ReadAllBytes(FixturePath); int offset;
            using (var pe = new PEReader(new MemoryStream(bytes, false)))
                offset = pe.PEHeaders.MetadataStartOffset + pe.GetMetadataReader().GetTableMetadataOffset(TableIndex.Module);
            Fixtures.U16(bytes, offset + 2, ushort.MaxValue);
            var result = Inspect(bytes); Check.True(result.Module is null); Check.Equal("partial", result.Coverage.State);
            Check.Equal("Rea.ManagedFixture", result.Assembly!.Name); Check.True(result.References.Count > 0);
        });
        foreach (var terminated in new[] { false, true })
            yield return Case.Sync($"Malformed module string is rejected at its owning heap boundary, terminated={terminated}", () =>
            {
                var bytes = File.ReadAllBytes(FixturePath); int row; int heap; int size;
                using (var pe = new PEReader(new MemoryStream(bytes, false)))
                {
                    var metadata = pe.GetMetadataReader(); row = pe.PEHeaders.MetadataStartOffset + metadata.GetTableMetadataOffset(TableIndex.Module);
                    heap = pe.PEHeaders.MetadataStartOffset + metadata.GetHeapMetadataOffset(HeapIndex.String);
                    var root = pe.GetMetadata().GetContent().AsSpan();
                    var streamName = root.IndexOf("#Strings\0"u8);
                    Check.True(streamName >= 8);
                    size = (int)BinaryPrimitives.ReadUInt32LittleEndian(root[(streamName - 4)..]);
                }
                var index = size - (terminated ? 2 : 1); Fixtures.U16(bytes, row + 2, (ushort)index);
                bytes[heap + index] = terminated ? (byte)0xff : (byte)'A';
                if (terminated) bytes[heap + index + 1] = 0;
                var result = Inspect(bytes); Check.True(result.Module is null); Check.Equal("partial", result.Coverage.State);
            });
        yield return Case.Sync("Malformed metadata root retains admitted PE and CLI declarations", () =>
        {
            var bytes = File.ReadAllBytes(FixturePath); int offset;
            using (var pe = new PEReader(new MemoryStream(bytes, false))) offset = pe.PEHeaders.MetadataStartOffset;
            Fixtures.U32(bytes, offset, 0);
            var result = Inspect(bytes); Check.Equal("malformed", result.Classification.Status); Check.Equal("partial", result.Coverage.State);
            Check.True(result.Pe.Cli is not null); Check.True(result.Assembly is null); Check.Equal("malformed", result.Metadata.Status);
        });
        yield return Case.Sync("Native PE reports unavailable managed inventory", () =>
        {
            var bytes = File.ReadAllBytes(FixturePath);
            using (var pe = new PEReader(new MemoryStream(bytes, false)))
            {
                var offset = pe.PEHeaders.PEHeaderStartOffset + 96 + 14 * 8;
                Fixtures.U32(bytes, offset, 0); Fixtures.U32(bytes, offset + 4, 0);
            }
            var result = Inspect(bytes); Check.Equal("not-managed", result.Classification.Status);
            Check.Equal("unavailable", result.Coverage.State); Check.True(result.Module is null);
        });
        foreach (var (flags, architecture) in new (uint, string)[] { (1, "anycpu"), (3, "x86"), (0x20003, "anycpu-prefer-32"), (0x20001, "anycpu"), (0, "x86") })
            yield return Case.Sync($"CLI bitness field 0x{flags:x} derives {architecture}", () =>
            {
                var bytes = File.ReadAllBytes(FixturePath); int offset;
                using (var pe = new PEReader(new MemoryStream(bytes, false))) offset = pe.PEHeaders.CorHeaderStartOffset;
                Fixtures.U32(bytes, offset + 16, flags);
                Check.Equal(architecture, Inspect(bytes).Classification.ManagedArchitecture);
            });
        yield return Case.Sync("Managed snapshot owns identity and isolates subsequent caller mutations", () =>
        {
            var bytes = File.ReadAllBytes(FixturePath); var expected = Identity(bytes);
            var snapshot = ManagedArtifactSnapshot.Create("caller-selected.dll", bytes);
            Array.Clear(bytes); Check.Equal(expected.Sha256, snapshot.Identity.Sha256);
            Check.Equal("managed", Check.Success(new ManagedArtifactInspector().Inspect(snapshot, default)).Classification.Status);
            using var stream = snapshot.OpenRead(); Check.True(!stream.CanWrite);
        });
        yield return Case.Sync("ReadyToRun signature with ILONLY clear does not imply mixed CLR/native", () =>
        {
            var bytes = File.ReadAllBytes(FixturePath); int cliOffset; int rva; int fileOffset;
            using (var pe = new PEReader(new MemoryStream(bytes, false)))
            {
                cliOffset = pe.PEHeaders.CorHeaderStartOffset;
                rva = pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress;
                fileOffset = Map(pe, rva);
            }
            Fixtures.U32(bytes, cliOffset + 16, 0); Fixtures.U32(bytes, cliOffset + 64, (uint)rva);
            Fixtures.U32(bytes, cliOffset + 68, 4); "RTR\0"u8.CopyTo(bytes.AsSpan(fileOffset));
            var result = Inspect(bytes); Check.True(result.Pe.Cli!.ReadyToRunSignature == true);
            Check.Equal("modern-dotnet", result.Classification.RuntimeFamily); Check.Equal("cil-and-ready-to-run", result.Classification.Implementation);
        });
        yield return Case.Sync("Unmappable native header retains unknown ReadyToRun identity", () =>
        {
            var bytes = File.ReadAllBytes(FixturePath); int cli;
            using (var pe = new PEReader(new MemoryStream(bytes, false))) cli = pe.PEHeaders.CorHeaderStartOffset;
            Fixtures.U32(bytes, cli + 16, 0); Fixtures.U32(bytes, cli + 64, 0x7fffffff); Fixtures.U32(bytes, cli + 68, 4);
            var result = Inspect(bytes); Check.True(result.Pe.Cli!.ReadyToRunSignature is null);
            Check.Equal("unknown", result.Classification.Implementation); Check.True(result.Classification.RuntimeFamily != "mixed-clr-native");
            Check.Equal("partial", result.Coverage.State); Check.True(result.Assembly is not null);
        });
        yield return new("Managed file budget and cancellation fail before projection", async () =>
        {
            var service = new ManagedInspectionService(new ManagedArtifactInspector());
            Check.Equal("resource-limit", Check.Failure(await service.InspectAsync(FixturePath, 1)).Code);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Check.Equal("cancelled", Check.Failure(await service.InspectAsync(FixturePath, cancellationToken: cancelled.Token)).Code);
            Check.Equal("invalid-input", Check.Failure(await service.InspectAsync("bad\0path")).Code);
        });
        yield return new("Managed CLI emits canonical snake_case and explicit unknowns", async () =>
        {
            using var output = new StringWriter(); using var error = new StringWriter();
            Check.Equal(0, await CliApplication.RunAsync(["inspect-managed-artifact", FixturePath], output, error));
            using var json = JsonDocument.Parse(output.ToString()); var value = json.RootElement.GetProperty("value");
            Check.Equal("managed", value.GetProperty("classification").GetProperty("status").GetString());
            Check.Equal(JsonValueKind.Null, value.GetProperty("module").GetProperty("enc_id").ValueKind);
            Check.Equal(FixturePath, value.GetProperty("artifact").GetProperty("path").GetString()); Check.Equal("", error.ToString());
        });
        foreach (var args in new[] { new[] { "inspect-managed-artifact" }, new[] { "inspect-managed-artifact", "x", "--max-file-bytes", "0" }, new[] { "inspect-managed-artifact", "x", "--header-bytes", "64" } })
            yield return new($"Managed CLI rejects syntax {string.Join(' ', args)}", async () =>
            { using var output = new StringWriter(); using var error = new StringWriter(); Check.Equal(2, await CliApplication.RunAsync(args, output, error)); });
        yield return new("Real managed CLI subprocess reads a non-executable fixture", async () =>
        {
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(typeof(ManagedTests).Assembly.Location, "runtimeconfig.json"), typeof(CliApplication).Assembly.Location, "inspect-managed-artifact", FixturePath }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token); var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token); Check.Equal(0, process.ExitCode); Check.Equal("", await stderr);
                using var json = JsonDocument.Parse(await stdout); Check.Equal("Rea.ManagedFixture", json.RootElement.GetProperty("value").GetProperty("assembly").GetProperty("name").GetString());
            }
            finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
        });
        yield return Case.Sync("Managed malformed-image mutations return expected outcomes", () =>
        {
            var original = File.ReadAllBytes(FixturePath); var random = new Random(132);
            for (var i = 0; i < 300; i++)
            {
                var bytes = (byte[])original.Clone();
                for (var j = 0; j < 3; j++) bytes[random.Next(bytes.Length)] = (byte)random.Next(256);
                _ = new ManagedArtifactInspector().Inspect(ManagedArtifactSnapshot.Create("mutated.dll", bytes), default);
            }
        });
    }

    private static int Map(PEReader pe, int rva)
    {
        foreach (var section in pe.PEHeaders.SectionHeaders)
            if (rva >= section.VirtualAddress && rva - section.VirtualAddress < section.SizeOfRawData)
                return section.PointerToRawData + rva - section.VirtualAddress;
        throw new InvalidOperationException("Fixture RVA is not backed by file bytes");
    }
}
