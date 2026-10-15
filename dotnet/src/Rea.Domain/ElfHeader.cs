namespace Rea.Domain;

public sealed record ElfFixedHeader(int ElfClass, string ByteOrder, ushort HeaderSize);

public static class ElfHeader
{
    public static Result<ElfFixedHeader, string> ReadFixedHeader(ReadOnlySpan<byte> bytes, long totalBytes)
    {
        Result<ElfFixedHeader, string> Fail(string reason) => new Result<ElfFixedHeader, string>.Failure(reason);
        if (bytes.Length >= 4 && !bytes[..4].SequenceEqual("\u007fELF"u8)) return Fail("not an ELF image");
        if (bytes.Length < 16) return Fail("ELF identification is truncated");
        if (bytes[4] is not (1 or 2)) return Fail("unsupported ELF class");
        if (bytes[5] is not (1 or 2)) return Fail("unsupported ELF endianness");
        if (bytes[6] != 1) return Fail("unsupported ELF identification version");
        var elfClass = bytes[4] == 1 ? 32 : 64;
        var minimum = elfClass == 32 ? 52 : 64;
        if (totalBytes < bytes.Length || totalBytes < minimum || bytes.Length < minimum)
            return Fail($"truncated ELF header: an ELF{elfClass} header needs {minimum} bytes; the file has {totalBytes}");
        var reader = new EndianReader(bytes, bytes[5] == 1);
        if (reader.U32(20) != 1) return Fail("unsupported ELF header version");
        var size = reader.U16(elfClass == 32 ? 40 : 52);
        if (size < minimum || size > totalBytes) return Fail("invalid ELF header size");
        return new Result<ElfFixedHeader, string>.Success(new(elfClass, bytes[5] == 1 ? "little" : "big", size));
    }

    internal static Result<ExecutableMetadata, string> Parse(ReadOnlySpan<byte> bytes, long totalBytes)
    {
        var fixedHeader = ReadFixedHeader(bytes, totalBytes);
        if (fixedHeader is Result<ElfFixedHeader, string>.Failure error)
            return ExecutableHeader.Fail(error.Error);
        var header = ((Result<ElfFixedHeader, string>.Success)fixedHeader).Value;
        var reader = new EndianReader(bytes, header.ByteOrder == "little");
        var architecture = reader.U16(18) switch
        {
            3 => "x86", 62 => "x86_64", 8 => "mips", 20 => "powerpc",
            40 => "arm", 183 => "arm64", 243 => "riscv", _ => null
        };
        if (architecture is null) return ExecutableHeader.Fail("unsupported ELF architecture");
        return ExecutableHeader.Ok(architecture is "mips" or "riscv" or "powerpc"
            ? new ElfFamilyExecutableMetadata(architecture,
                new(header.ElfClass, header.ByteOrder, reader.U16(16), reader.U32(header.ElfClass == 32 ? 36 : 48)))
            : new SimpleExecutableMetadata("elf", architecture));
    }
}
