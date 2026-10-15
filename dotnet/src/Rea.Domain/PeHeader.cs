using System.Text;

namespace Rea.Domain;

public sealed record PeMachine(string Architecture, bool NativeOsOverride);
public sealed record PeDebugDirectory(uint DebugRva, uint DebugSize, uint SizeOfHeaders);

public static class PeHeader
{
    private static string? Architecture(uint machine) => machine switch
    {
        0x014c => "x86", 0x8664 => "x86_64", 0x01c0 or 0x01c2 or 0x01c4 => "arm", 0xaa64 => "arm64", _ => null
    };

    public static PeMachine? DecodeMachine(ushort machine)
    {
        if (Architecture(machine) is { } direct) return new(direct, false);
        ReadOnlySpan<uint> overrides = [0x4644, 0xadc4, 0xadc5, 0x7b79, 0x1993, 0x1992];
        foreach (var os in overrides)
            if (Architecture(machine ^ os) is { } architecture) return new(architecture, true);
        return null;
    }

    public static Result<ExecutableMetadata, string> ParseRecord(ReadOnlySpan<byte> record, PeCodeViewRecord? codeView = null)
    {
        if (record.Length < 24 || !record[..4].SequenceEqual("PE\0\0"u8)) return ExecutableHeader.Fail("invalid or truncated PE header");
        var reader = new EndianReader(record);
        var rawMachine = reader.U16(4);
        var machine = DecodeMachine(rawMachine);
        if (machine is null) return ExecutableHeader.Fail($"unsupported PE architecture (COFF machine 0x{rawMachine:x4})");
        var optionalSize = reader.U16(20);
        if (optionalSize > 4096 || record.Length < 24 + optionalSize) return ExecutableHeader.Fail("invalid or truncated PE optional header");
        var optional = record.Slice(24, optionalSize);
        if (optional.Length < 2) return ExecutableHeader.Fail("truncated PE optional header");
        var optReader = new EndianReader(optional);
        var magic = optReader.U16(0);
        var expectedMagic = machine.Architecture is "x86_64" or "arm64" ? 0x20b : 0x10b;
        if (magic != expectedMagic) return ExecutableHeader.Fail("PE optional-header magic does not match its architecture");
        var directoryOffset = magic == 0x20b ? 112 : 96;
        if (optional.Length < directoryOffset) return ExecutableHeader.Fail("truncated PE optional header");
        var directoryCount = optReader.U32(directoryOffset - 4);
        if (directoryCount > (optional.Length - directoryOffset) / 8)
            return ExecutableHeader.Fail("truncated PE data-directory table");
        var managed = directoryCount > 14 && (optReader.U32(directoryOffset + 14 * 8) != 0 || optReader.U32(directoryOffset + 14 * 8 + 4) != 0);
        if (machine.NativeOsOverride && !managed) return ExecutableHeader.Fail("unsupported PE architecture");
        var characteristics = reader.U16(22);
        return ExecutableHeader.Ok(new PeExecutableMetadata(rawMachine, machine.Architecture,
            (characteristics & 2) == 0 ? "non-executable" : (characteristics & 0x2000) == 0 ? "application" : "shared-library",
            managed, codeView));
    }

    public static PeCodeViewRecord? ParseCodeViewData(ReadOnlySpan<byte> bytes, uint sizeOfData)
    {
        if (sizeOfData < 16 || bytes.Length < 16) return null;
        var rsds = bytes[..4].SequenceEqual("RSDS"u8);
        var nb10 = bytes[..4].SequenceEqual("NB10"u8);
        if (!rsds && !nb10) return null;
        var pathOffset = rsds ? 24 : 16;
        if (sizeOfData <= pathOffset || bytes.Length <= pathOffset) return null;
        var end = bytes[pathOffset..].IndexOf((byte)0);
        if (end <= 0 || (long)pathOffset + end >= sizeOfData) return null;
        var path = Encoding.UTF8.GetString(bytes.Slice(pathOffset, end));
        var filename = path[(Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\')) + 1)..];
        var reader = new EndianReader(bytes);
        return new(path, filename, rsds ? new Guid(bytes.Slice(4, 16)).ToString() : null, reader.U32(rsds ? 20 : 12));
    }

    public static PeDebugDirectory? DebugDirectoryEntry(ReadOnlySpan<byte> optional)
    {
        if (optional.Length < 2) return null;
        var reader = new EndianReader(optional);
        var magic = reader.U16(0);
        if (magic is not (0x10b or 0x20b)) return null;
        var offset = magic == 0x20b ? 112 : 96;
        if (optional.Length < offset || reader.U32(offset - 4) <= 6 || optional.Length < offset + 56) return null;
        var rva = reader.U32(offset + 48);
        var size = reader.U32(offset + 52);
        return rva == 0 || size < 28 ? null : new(rva, size, reader.U32(60));
    }

    public static long? RvaToFileOffset(uint rva, ReadOnlySpan<byte> sections, ushort count, uint sizeOfHeaders, uint size = 1)
    {
        if (rva < sizeOfHeaders && size <= sizeOfHeaders - rva) return rva;
        var reader = new EndianReader(sections);
        for (var i = 0; i < count; i++)
        {
            var offset = i * 40;
            if (sections.Length < offset + 40) break;
            var address = reader.U32(offset + 12);
            var rawSize = reader.U32(offset + 16);
            var pointer = reader.U32(offset + 20);
            if (pointer > 0 && rva >= address && rva - address < rawSize && size <= rawSize - (rva - address))
                return (long)pointer + rva - address;
        }
        return null;
    }

    public static PeCodeViewRecord? ReadCodeView(ReadOnlySpan<byte> bytes, int peOffset)
    {
        if (peOffset < 0 || peOffset > bytes.Length - 24) return null;
        var header = new EndianReader(bytes[peOffset..]);
        var count = header.U16(6);
        var optionalSize = header.U16(20);
        if (optionalSize > 4096 || bytes.Length - peOffset - 24 < optionalSize) return null;
        var directory = DebugDirectoryEntry(bytes.Slice(peOffset + 24, optionalSize));
        if (directory is null) return null;
        var sectionOffset = peOffset + 24 + optionalSize;
        var sectionSize = count * 40;
        if (bytes.Length - sectionOffset < sectionSize) return null;
        var sections = bytes.Slice(sectionOffset, sectionSize);
        var debugOffset = RvaToFileOffset(directory.DebugRva, sections, count, directory.SizeOfHeaders, Math.Min(directory.DebugSize, 4096));
        if (debugOffset is null || debugOffset > bytes.Length - 28) return null;
        var entries = Math.Min((long)directory.DebugSize, bytes.Length - debugOffset.Value) / 28;
        for (var i = 0; i < entries; i++)
        {
            var entry = new EndianReader(bytes[((int)debugOffset.Value + i * 28)..]);
            if (entry.U32(12) != 2) continue;
            var size = entry.U32(16);
            long? pointer = entry.U32(24);
            if (pointer == 0) pointer = RvaToFileOffset(entry.U32(20), sections, count, directory.SizeOfHeaders, Math.Min(size, 4096));
            if (pointer is null || size < 16 || pointer > bytes.Length - Math.Min(size, 24)) continue;
            var available = (int)Math.Min(size, bytes.Length - pointer.Value);
            var parsed = ParseCodeViewData(bytes.Slice((int)pointer.Value, available), size);
            if (parsed is not null) return parsed;
        }
        return null;
    }
}
