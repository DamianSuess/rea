using System.Text.Json.Serialization;

namespace Rea.Domain;

[JsonDerivedType(typeof(SimpleExecutableMetadata))]
[JsonDerivedType(typeof(ElfFamilyExecutableMetadata))]
[JsonDerivedType(typeof(PeExecutableMetadata))]
[JsonDerivedType(typeof(UniversalMachOMetadata))]
public abstract record ExecutableMetadata(
    string Format,
    string Architecture,
    IReadOnlyList<string> AvailableArchitectures);

public sealed record SimpleExecutableMetadata(string Format, string Architecture)
    : ExecutableMetadata(Format, Architecture, Array.AsReadOnly(new[] { Architecture }));

public sealed record ElfFamilyMetadata(int ElfClass, string ByteOrder, ushort Type, uint Flags);

// Omitted family members mean unexamined, never an assertion that ABI records are absent.
public sealed record ElfFamilyExecutableMetadata : ExecutableMetadata
{
    public ElfFamilyMetadata? Mips { get; }
    public ElfFamilyMetadata? Riscv { get; }
    public ElfFamilyMetadata? Powerpc { get; }

    public ElfFamilyExecutableMetadata(string architecture, ElfFamilyMetadata metadata)
        : base("elf", architecture, Array.AsReadOnly(new[] { architecture }))
    {
        switch (architecture)
        {
            case "mips": Mips = metadata; break;
            case "riscv": Riscv = metadata; break;
            case "powerpc": Powerpc = metadata; break;
            default: throw new ArgumentOutOfRangeException(nameof(architecture));
        }
    }
}

public sealed record PeCodeViewRecord(string PdbPath, string PdbFileName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Guid, uint Age);
public sealed record PeExecutableMetadata(
    ushort PeMachine, string Architecture, string ExecutableRole, bool Managed,
    PeCodeViewRecord? CodeView)
    : ExecutableMetadata("pe", Architecture, Array.AsReadOnly(new[] { Architecture }));

public sealed record UniversalMachOSlice(long Offset, long Size);
public sealed record UniversalMachOMetadata(
    string Architecture, IReadOnlyList<string> AvailableArchitectures,
    UniversalMachOSlice UniversalSlice)
    : ExecutableMetadata("mach-o-universal", Architecture, AvailableArchitectures);
