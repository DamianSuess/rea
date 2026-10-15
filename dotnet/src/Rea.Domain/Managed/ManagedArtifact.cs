namespace Rea.Domain.Managed;

public sealed record ManagedArtifactIdentity(string Path, string Sha256, long ByteLength, string Format = "pe");
public sealed record ManagedParseIssue(string Code, string Scope, long? Offset, string Detail);
public sealed record ManagedCoverage(string State, IReadOnlyList<ManagedParseIssue> Issues);
public sealed record ManagedClassificationFact(string Code, string Detail, long? FileOffset);
public sealed record ManagedClassification(string Status, string Container, string RuntimeFamily,
    string Implementation, string ManagedArchitecture, IReadOnlyList<ManagedClassificationFact> Evidence);
public sealed record ManagedEntryPoint(string Kind, string? Value);
public sealed record ManagedCliHeader(long HeaderOffset, uint HeaderSize, string RuntimeVersion, uint Flags,
    IReadOnlyList<string> FlagNames, ManagedEntryPoint EntryPoint, uint MetadataRva, uint MetadataSize,
    uint ResourcesRva, uint ResourcesSize, uint StrongNameRva, uint StrongNameSize,
    uint ManagedNativeHeaderRva, uint ManagedNativeHeaderSize, bool? ReadyToRunSignature);
public sealed record ManagedPeFacts(ushort Machine, string MachineHex, string Architecture,
    string OptionalHeader, int SectionCount, ushort Characteristics, ManagedCliHeader? Cli);
public sealed record ManagedMetadataFacts(string Status, string? Version,
    IReadOnlyList<string> StreamNames, IReadOnlyDictionary<string, int> TableRowCounts);
public sealed record ManagedModuleIdentity(string Name, int Generation, string? Mvid, string? EncId,
    string? EncBaseId, string Token, long RowOffset);
public sealed record ManagedPublicKey(string Kind, int ByteLength, string? Sha256, string? Token);
public sealed record ManagedAssemblyIdentity(string Name, string Version, string? Culture, uint Flags,
    uint HashAlgorithm, ManagedPublicKey PublicKey, string Token, long RowOffset);
public sealed record ManagedAssemblyReference(string Name, string Version, string? Culture, uint Flags,
    ManagedPublicKey PublicKeyOrToken, string? HashValueSha256, int HashValueLength, string Token, long RowOffset);
public sealed record ManagedResource(string Name, uint Flags, string Visibility, string? ImplementationToken,
    bool Embedded, uint DeclaredOffset, int? DataLength, string? DataSha256, string Token, long RowOffset);
public sealed record ManagedCustomAttribute(string ParentToken, string? ConstructorToken, string? TypeName,
    int ValueLength, string ValueSha256, string? DecodedFixedString, string Token, long RowOffset);
public sealed record ManagedArtifactInspection(ManagedArtifactIdentity Artifact, ManagedPeFacts Pe,
    ManagedClassification Classification, ManagedMetadataFacts Metadata, ManagedModuleIdentity? Module,
    ManagedAssemblyIdentity? Assembly, IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<ManagedAssemblyReference> References, IReadOnlyList<ManagedResource> Resources,
    IReadOnlyList<ManagedCustomAttribute> Attributes, ManagedCoverage Coverage, IReadOnlyList<string> Limitations);
