using System.Buffers.Binary;

namespace Rea.Domain;

public static class ExecutableHeader
{
    public static Result<ExecutableMetadata, string> Parse(
        ReadOnlySpan<byte> bytes, string hostArchitecture, long? fileSize = null,
        ReadOnlySpan<byte> mzWindowsHeaderSignature = default)
    {
        var size = fileSize ?? bytes.Length;
        if (size < bytes.Length) return Fail("file length contradicts retained bytes");
        if (bytes.Length >= 4 && bytes[..4].SequenceEqual("\u007fELF"u8)) return ElfHeader.Parse(bytes, size);
        if (bytes.Length >= 2 && bytes[..2].SequenceEqual("MZ"u8)) return DosMz.ParseExecutable(bytes, size, mzWindowsHeaderSignature);
        if (bytes.Length < 8) return Fail("truncated or unsupported binary header");
        var magic = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        if (MachOHeader.IsThinMagic(magic)) return MachOHeader.ParseThin(bytes, magic, size);
        if (MachOHeader.IsFatMagic(magic)) return MachOHeader.ParseFat(bytes, hostArchitecture, size, magic);
        return Fail("unsupported binary format");
    }

    public static Result<ExecutableMetadata, string> ParseDosCom(long length) =>
        length is >= 1 and <= 0xff00
            ? Ok(new SimpleExecutableMetadata("dos-com", "x86"))
            : Fail("DOS COM requires 1..65280 file bytes (64 KiB segment minus the 256-byte PSP prefix)");

    internal static Result<ExecutableMetadata, string> Ok(ExecutableMetadata value) =>
        new Result<ExecutableMetadata, string>.Success(value);
    internal static Result<ExecutableMetadata, string> Fail(string reason) =>
        new Result<ExecutableMetadata, string>.Failure(reason);
}
