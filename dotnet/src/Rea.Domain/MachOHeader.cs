namespace Rea.Domain;

internal static class MachOHeader
{
    internal static bool IsThinMagic(uint magic) => magic is 0xfeedface or 0xfeedfacf or 0xcefaedfe or 0xcffaedfe;
    internal static bool IsFatMagic(uint magic) => magic is 0xcafebabe or 0xcafebabf or 0xbebafeca or 0xbfbafeca;
    private static string? Architecture(uint cpu) => cpu switch
    {
        7 => "x86", 0x01000007 => "x86_64", 12 => "arm", 0x0100000c => "arm64", _ => null
    };

    internal static Result<ExecutableMetadata, string> ParseThin(ReadOnlySpan<byte> bytes, uint magic, long fileSize)
    {
        var wide = magic is 0xfeedfacf or 0xcffaedfe;
        var headerSize = wide ? 32 : 28;
        if (bytes.Length < headerSize)
            return ExecutableHeader.Fail($"truncated Mach-O header: a {(wide ? 64 : 32)}-bit header needs {headerSize} bytes; the file has {fileSize}");
        var reader = new EndianReader(bytes, magic is 0xcefaedfe or 0xcffaedfe);
        var architecture = Architecture(reader.U32(4));
        if (architecture is null) return ExecutableHeader.Fail("unsupported Mach-O architecture");
        var commandBytes = reader.U32(20);
        if (headerSize + (long)commandBytes > fileSize)
            return ExecutableHeader.Fail($"truncated Mach-O load commands: the header declares {commandBytes} bytes after its {headerSize}-byte header; the file has {fileSize}");
        return ExecutableHeader.Ok(new SimpleExecutableMetadata("mach-o", architecture));
    }

    private sealed record Slice(int Index, uint Cpu, long Offset, long Size, uint Alignment);

    internal static Result<ExecutableMetadata, string> ParseFat(ReadOnlySpan<byte> bytes, string host, long fileSize, uint magic)
    {
        if (bytes.Length == fileSize && JvmClassFile.IsClassFile(bytes)) return ExecutableHeader.Fail("unsupported binary format");
        var little = magic is 0xbebafeca or 0xbfbafeca;
        var wide = magic is 0xcafebabf or 0xbfbafeca;
        var reader = new EndianReader(bytes, little);
        var count = reader.U32(4);
        if (count == 0) return ExecutableHeader.Fail("FAT header declares no architectures");
        if (count > 128) return ExecutableHeader.Fail("unsupported binary format");
        var entrySize = wide ? 32 : 20;
        var tableEnd = 8 + (int)count * entrySize;
        if (tableEnd > fileSize) return ExecutableHeader.Fail("unsupported binary format");
        if (tableEnd > bytes.Length) return ExecutableHeader.Fail("truncated or invalid FAT architecture table");
        var slices = new List<Slice>((int)count);
        for (var i = 0; i < count; i++)
        {
            var entry = 8 + i * entrySize;
            var offset = wide ? reader.U64(entry + 8) : reader.U32(entry + 8);
            var size = wide ? reader.U64(entry + 16) : reader.U32(entry + 12);
            if (offset > 9007199254740991 || size > 9007199254740991)
                return ExecutableHeader.Fail("FAT offset or size exceeds the safe range");
            slices.Add(new(i, reader.U32(entry), (long)offset, (long)size, reader.U32(entry + (wide ? 24 : 16))));
        }
        string? firstIssue = null;
        var hasKnownCpu = slices.Any(slice => slice.Cpu is 7 or 0x01000007 or 12 or 0x0100000c or 0x0200000c or 18 or 0x01000012);
        var missingHeader = false;
        var observedMachO = false;
        foreach (var slice in slices)
        {
            var issue = RangeIssue(slice, fileSize, tableEnd);
            firstIssue ??= issue;
            if (issue is not null || hasKnownCpu) continue;
            if (slice.Offset >= bytes.Length) { missingHeader = true; continue; }
            if (slice.Offset + 4 <= bytes.Length && IsThinMagic(new EndianReader(bytes, false).U32((int)slice.Offset))) observedMachO = true;
        }
        if (!hasKnownCpu && !observedMachO)
            return ExecutableHeader.Fail(missingHeader ? "FAT slice identity is outside the bounded prefix" : "FAT table does not identify a Mach-O image");
        if (firstIssue is not null) return ExecutableHeader.Fail(firstIssue);
        var architectures = new List<string>();
        var known = new Dictionary<string, Slice>(StringComparer.Ordinal);
        foreach (var slice in slices)
        {
            var architecture = Architecture(slice.Cpu);
            if (architecture is null || known.ContainsKey(architecture)) continue;
            architectures.Add(architecture);
            known.Add(architecture, slice);
        }
        var preferred = host switch { "x64" => "x86_64", "ia32" or "x86" => "x86", "arm64" => "arm64", "arm" => "arm", _ => null };
        if (preferred is null || !known.TryGetValue(preferred, out var selected))
            return ExecutableHeader.Fail($"FAT binary has no host-compatible {host} architecture");
        if (selected.Size < (preferred is "arm64" or "x86_64" ? 32 : 28))
            return ExecutableHeader.Fail($"FAT slice for {preferred} is shorter than its Mach-O header");
        return ExecutableHeader.Ok(new UniversalMachOMetadata(preferred, architectures.AsReadOnly(), new(selected.Offset, selected.Size)));
    }

    private static string? RangeIssue(Slice slice, long fileSize, int tableEnd)
    {
        if (slice.Size <= 0 || slice.Offset < tableEnd) return $"FAT architecture {slice.Index} has an invalid slice range";
        if (slice.Offset > fileSize || slice.Size > fileSize - slice.Offset) return $"FAT architecture {slice.Index} slice range extends beyond the file";
        if (slice.Alignment > 52) return $"FAT architecture {slice.Index} alignment exponent {slice.Alignment} is unsupported";
        if (slice.Offset % (1L << (int)slice.Alignment) != 0) return $"FAT architecture {slice.Index} offset is not aligned to 2^{slice.Alignment}";
        return null;
    }
}
