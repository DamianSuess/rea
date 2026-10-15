using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using Rea.Application;
using Rea.Domain;
using Rea.Domain.Managed;

namespace Rea.Managed;

/// <summary>Static metadata projection. Never uses Assembly.Load or invokes a target member.</summary>
public sealed class ManagedArtifactInspector : IManagedArtifactInspector
{
    public Result<ManagedArtifactInspection, InspectionError> Inspect(ManagedArtifactSnapshot target, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return Fail("cancelled", "Managed inspection was cancelled");
        var snapshot = target.Content;
        var identity = target.Identity;
        var header = ExecutableHeader.Parse(snapshot.Span, "x64");
        if (header is not Result<ExecutableMetadata, string>.Success { Value: PeExecutableMetadata decoded })
            return Fail("invalid-header", "Managed inspection requires a supported PE image");
        var peOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Span[60..]);
        var facts = new ManagedPeFacts(decoded.PeMachine, $"0x{decoded.PeMachine:x4}", decoded.Architecture,
            decoded.Architecture is "arm64" or "x86_64" ? "pe32-plus" : "pe32",
            BinaryPrimitives.ReadUInt16LittleEndian(snapshot.Span[(peOffset + 6)..]),
            BinaryPrimitives.ReadUInt16LittleEndian(snapshot.Span[(peOffset + 22)..]), null);
        ManagedModuleIdentity? module = null; ManagedAssemblyIdentity? assembly = null;
        IReadOnlyList<ManagedAssemblyReference> references = Array.Empty<ManagedAssemblyReference>();
        IReadOnlyList<ManagedResource> resources = Array.Empty<ManagedResource>();
        IReadOnlyList<ManagedCustomAttribute> attributes = Array.Empty<ManagedCustomAttribute>();
        IReadOnlyList<string> streams = Array.Empty<string>(); string? version = null;
        var rowCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        ManagedReadContext? context = null;
        var issues = new List<ManagedParseIssue>(); var exhausted = false;
        var nativeHeaderObserved = false;
        using var stream = target.OpenRead();
        try
        {
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            var image = new ManagedPeImage(pe, snapshot);
            var cli = pe.PEHeaders.CorHeader;
            if (cli is null)
            {
                var absent = !decoded.Managed;
                return Success(new(identity, facts,
                    new(absent ? "not-managed" : "malformed", "pe", "unknown", absent ? "not-managed" : "unknown", "unknown",
                        [new(absent ? "cli-directory-absent" : "cli-directory-malformed", absent ? "The PE has no CLI data directory." : "The PE declares a CLI directory that could not be read.", null)]),
                    new(absent ? "absent" : "malformed", null, [], rowCounts), null, null, [], [], [], [],
                    new("unavailable", []), ["Managed metadata is unavailable; no target code was loaded."]));
            }
            var rawCli = pe.PEHeaders.CorHeaderStartOffset;
            var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Span[rawCli..]);
            if (headerSize < 72 || headerSize > pe.PEHeaders.PEHeader!.CorHeaderTableDirectory.Size || rawCli > snapshot.Length - (long)headerSize)
                throw new BadImageFormatException("CLI header size contradicts its directory or file extent");
            var nativeDirectory = cli.ManagedNativeHeaderDirectory;
            bool? readyToRun = false;
            if (nativeDirectory.RelativeVirtualAddress != 0 || nativeDirectory.Size != 0)
            {
                nativeHeaderObserved = true;
                readyToRun = null;
                if (nativeDirectory.Size < 4) issues.Add(new("invalid-directory", "cli.managed-native-header", rawCli + 64, "Managed native directory is too short for its signature"));
                else
                {
                    // Validate file-backed mapping using the same context as the inventory below.
                    try
                    {
                        var offset = image.FileOffset(nativeDirectory.RelativeVirtualAddress, nativeDirectory.Size);
                        readyToRun = snapshot.Span.Slice((int)offset, 4).SequenceEqual("RTR\0"u8);
                    }
                    catch (BadImageFormatException ex) { issues.Add(new("invalid-directory", "cli.managed-native-header", rawCli + 64, ex.Message)); }
                }
            }
            var flags = (uint)cli.Flags;
            var entry = unchecked((uint)cli.EntryPointTokenOrRelativeVirtualAddress);
            facts = facts with { Cli = new(rawCli, headerSize, $"{cli.MajorRuntimeVersion}.{cli.MinorRuntimeVersion}", flags,
                FlagNames(flags), entry == 0 ? new("none", null) : new((flags & 16) != 0 ? "native-rva" : "metadata-token", $"0x{entry:x8}"),
                unchecked((uint)cli.MetadataDirectory.RelativeVirtualAddress), unchecked((uint)cli.MetadataDirectory.Size),
                unchecked((uint)cli.ResourcesDirectory.RelativeVirtualAddress), unchecked((uint)cli.ResourcesDirectory.Size),
                unchecked((uint)cli.StrongNameSignatureDirectory.RelativeVirtualAddress), unchecked((uint)cli.StrongNameSignatureDirectory.Size),
                unchecked((uint)nativeDirectory.RelativeVirtualAddress), unchecked((uint)nativeDirectory.Size), readyToRun) };
            var metadata = pe.GetMetadataReader(MetadataReaderOptions.None, new MetadataStringDecoder(new UTF8Encoding(false, true)));
            image.FileOffset(cli.MetadataDirectory.RelativeVirtualAddress, cli.MetadataDirectory.Size);
            var declarations = ManagedStreamHeaders.Read(pe.GetMetadata().GetContent().AsSpan());
            streams = Array.AsReadOnly(declarations.Select(declaration => declaration.Name).ToArray());
            context = new(image, metadata, declarations.SingleOrDefault(declaration => declaration.Name == "#Strings"), cancellationToken);
            version = metadata.MetadataVersion;
            foreach (var table in Enum.GetValues<TableIndex>())
            {
                var rows = metadata.GetTableRowCount(table);
                if (rows > 0) rowCounts[table.ToString()] = rows;
            }
            var inventory = new ManagedInventory(context);
            references = inventory.AdmittedReferences.AsReadOnly();
            resources = inventory.AdmittedResources.AsReadOnly();
            attributes = inventory.AdmittedAttributes.AsReadOnly();
            module = inventory.Module(); assembly = inventory.Assembly();
            inventory.References(); inventory.Resources(); inventory.Attributes();
        }
        catch (ManagedCapacityException ex)
        { exhausted = true; issues.Add(new("resource-limit", "metadata.representation", null, ex.Message)); }
        catch (BadImageFormatException ex) { issues.Add(new("invalid-metadata-root", "metadata", null, ex.Message)); }
        catch (DecoderFallbackException ex) { issues.Add(new("invalid-string", "metadata", null, ex.Message)); }
        catch (ArgumentOutOfRangeException ex) { issues.Add(new("invalid-directory", "metadata", null, ex.Message)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Fail("cancelled", "Managed inspection was cancelled"); }
        if (context is not null) issues.AddRange(context.Issues);
        var frameworks = attributes.Where(attribute => attribute.ParentToken == "0x20000001"
            && attribute.TypeName == "System.Runtime.Versioning.TargetFrameworkAttribute" && attribute.DecodedFixedString is not null)
            .Select(attribute => attribute.DecodedFixedString!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var classification = Classify(facts, frameworks, references, rowCounts, version is not null, exhausted);
        var limitations = new List<string>
        {
            "Static PE/CLI declarations only; assembly dependencies and target code were never loaded or executed.",
            "The digest identifies the retained full-file snapshot; it does not establish an atomic filesystem snapshot.",
            "Method bodies, decoded signatures and general custom-attribute argument values were not inspected.",
            "Runtime-family classification is derived from declarations; it does not prove the runtime used by a process."
        };
        if (nativeHeaderObserved && facts.Cli?.ReadyToRunSignature != true)
            limitations.Add("A managed native header was declared without an admitted ReadyToRun signature; its meaning remains unknown.");
        return Success(new(identity, facts, classification, new(version is null ? "malformed" : issues.Count == 0 ? "complete" : "partial", version, streams, rowCounts),
            module, assembly, frameworks, references, resources, attributes, new(issues.Count == 0 ? "complete" : "partial", issues.AsReadOnly()), limitations.AsReadOnly()));
    }

    private static ManagedClassification Classify(ManagedPeFacts pe, IReadOnlyList<string> frameworks,
        IReadOnlyList<ManagedAssemblyReference> references, IReadOnlyDictionary<string, int> rows, bool metadataObserved, bool exhausted)
    {
        var cli = pe.Cli; var flags = cli?.Flags ?? 0;
        var facts = new List<ManagedClassificationFact>();
        if (cli is null) return new("malformed", "pe", "unknown", "unknown", "unknown", facts);
        facts.Add(new("cli-header-observed", "A file-backed CLI header was observed.", cli.HeaderOffset));
        var mixed = (flags & 1) == 0 && cli.ReadyToRunSignature == false;
        var runtime = "unknown";
        if (mixed) { runtime = "mixed-clr-native"; facts.Add(new("cli-il-only-clear", "ILONLY is clear without an admitted ReadyToRun signature.", cli.HeaderOffset + 16)); }
        else if (references.Any(reference => reference.Name == "UnityEngine" || reference.Name.StartsWith("UnityEngine.", StringComparison.Ordinal)))
        { runtime = "unity-mono"; facts.Add(new("unity-reference-observed", "The AssemblyRef table declares a UnityEngine reference.", null)); }
        else if (frameworks.Any(value => value.StartsWith(".NETFramework,", StringComparison.Ordinal)))
        { runtime = "dotnet-framework"; facts.Add(new("target-framework-observed", "An assembly-level TargetFrameworkAttribute names .NET Framework.", null)); }
        else if (cli.ReadyToRunSignature == true || frameworks.Any(value => value.StartsWith(".NETCoreApp,", StringComparison.Ordinal)))
        { runtime = "modern-dotnet"; facts.Add(new(cli.ReadyToRunSignature == true ? "ready-to-run-signature-observed" : "target-framework-observed", "Declarations identify modern .NET.", null)); }
        var architecture = pe.Architecture;
        if (architecture == "x86" && (flags & 1) != 0)
            architecture = (flags & 0x20002) switch { 2 => "x86", 0x20002 => "anycpu-prefer-32", _ => "anycpu" };
        facts.Add(new("managed-architecture-derived", $"Managed architecture {architecture} is derived from PE machine and CLI flags.", cli.HeaderOffset + 16));
        if (cli.ReadyToRunSignature is null)
            facts.Add(new("managed-native-signature-unavailable", "The declared managed native header could not be examined; ReadyToRun identity remains unknown.", cli.HeaderOffset + 64));
        var implementation = !metadataObserved ? "unknown" : cli.ReadyToRunSignature == true ? "cil-and-ready-to-run"
            : (flags & 1) == 0 ? cli.ReadyToRunSignature is null ? "unknown" : "cpp-cli-mixed"
            : rows.ContainsKey("MethodDef") ? "cil" : "metadata-only";
        return new(metadataObserved || exhausted ? "managed" : "malformed", "pe", runtime, implementation, architecture, facts.AsReadOnly());
    }

    private static IReadOnlyList<string> FlagNames(uint flags)
    {
        var names = new List<string>();
        foreach (var (mask, name) in new (uint, string)[] { (1, "il-only"), (2, "32-bit-required"), (4, "il-library"), (8, "strong-name-signed"), (16, "native-entry-point"), (0x10000, "track-debug-data"), (0x20000, "32-bit-preferred") })
            if ((flags & mask) != 0) names.Add(name);
        return names.AsReadOnly();
    }
    private static Result<ManagedArtifactInspection, InspectionError> Fail(string code, string message) => new Result<ManagedArtifactInspection, InspectionError>.Failure(new(code, message));
    private static Result<ManagedArtifactInspection, InspectionError> Success(ManagedArtifactInspection value) => new Result<ManagedArtifactInspection, InspectionError>.Success(value);
}
