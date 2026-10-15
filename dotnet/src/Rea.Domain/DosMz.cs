namespace Rea.Domain;

public abstract record MzWindowsHeaderObservation
{
    private MzWindowsHeaderObservation() { }
    public sealed record Absent : MzWindowsHeaderObservation;
    public sealed record HeaderNeeded : MzWindowsHeaderObservation;
    public sealed record SignatureNeeded(uint Offset) : MzWindowsHeaderObservation;
    public sealed record Declared(uint Offset, string Signature) : MzWindowsHeaderObservation;
}

public sealed record DosMzHeader(
    int HeaderBytes, long ImageBytes, long ModuleBytes, long OverlayBytes,
    ushort RelocationCount, ushort RelocationTableOffset, ushort EntrySegment, ushort EntryOffset);

public static class DosMz
{
    public static MzWindowsHeaderObservation ObserveWindowsHeader(
        ReadOnlySpan<byte> bytes, long fileSize, ReadOnlySpan<byte> signatureAtOffset = default)
    {
        if (bytes.Length < 64) return fileSize >= 64
            ? new MzWindowsHeaderObservation.HeaderNeeded() : new MzWindowsHeaderObservation.Absent();
        var reader = new EndianReader(bytes);
        var headerBytes = reader.U16(8) * 16;
        var count = reader.U16(6);
        var tableStart = reader.U16(24);
        if (headerBytes != 0 && count > 0 && tableStart < 64 && tableStart + count * 4 > 60)
            return new MzWindowsHeaderObservation.Absent();
        var offset = reader.U32(60);
        if (offset == 0) return new MzWindowsHeaderObservation.Absent();
        var available = !signatureAtOffset.IsEmpty ? signatureAtOffset
            : offset < bytes.Length ? bytes.Slice((int)offset, Math.Min(4, bytes.Length - (int)offset)) : default;
        var signature = Signature(available);
        if (headerBytes == 0 || headerBytes >= 64 || signature is not ("other" or "unobserved"))
            return new MzWindowsHeaderObservation.Declared(offset, signature);
        if (signature == "other") return new MzWindowsHeaderObservation.Absent();
        var pePrefix = available.Length >= 2 && available[..2].SequenceEqual("PE"u8);
        return fileSize - offset < (pePrefix ? 4 : 2)
            ? new MzWindowsHeaderObservation.Absent()
            : new MzWindowsHeaderObservation.SignatureNeeded(offset);
    }

    private static string Signature(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2) return "unobserved";
        if (bytes[..2].SequenceEqual("NE"u8)) return "NE";
        if (bytes[..2].SequenceEqual("LE"u8)) return "LE";
        if (bytes[..2].SequenceEqual("LX"u8)) return "LX";
        if (!bytes[..2].SequenceEqual("PE"u8)) return "other";
        if (bytes.Length < 4) return "unobserved";
        return bytes[2] == 0 && bytes[3] == 0 ? "PE" : "other";
    }

    public static Result<DosMzHeader, string> Parse(
        ReadOnlySpan<byte> bytes, long fileSize, ReadOnlySpan<byte> signatureAtOffset = default)
    {
        Result<DosMzHeader, string> Fail(string reason) => new Result<DosMzHeader, string>.Failure(reason);
        if (bytes.Length < 28 || !bytes[..2].SequenceEqual("MZ"u8)) return Fail("invalid or truncated DOS MZ header");
        if (fileSize < bytes.Length) return Fail("invalid DOS MZ file length");
        var windows = ObserveWindowsHeader(bytes, fileSize, signatureAtOffset);
        if (windows is MzWindowsHeaderObservation.Declared) return Fail("MZ declares a Windows new header; it is not a DOS-only image");
        if (windows is MzWindowsHeaderObservation.SignatureNeeded) return Fail("MZ Windows new-header signature is outside the bounded prefix");
        var reader = new EndianReader(bytes);
        var lastPageBytes = reader.U16(2);
        var pages = reader.U16(4);
        if (pages == 0 || lastPageBytes > 511) return Fail("invalid DOS MZ page count or final-page size");
        var imageBytes = (pages - 1L) * 512 + (lastPageBytes == 0 ? 512 : lastPageBytes);
        var headerBytes = reader.U16(8) * 16;
        if (headerBytes < 28 || headerBytes >= imageBytes) return Fail("invalid DOS MZ header size or empty load module");
        if (imageBytes > fileSize) return Fail("truncated DOS MZ load module");
        var count = reader.U16(6);
        var tableOffset = reader.U16(24);
        var tableEnd = tableOffset + count * 4;
        if (count > 0 && (tableOffset < 28 || tableEnd > headerBytes)) return Fail("DOS MZ relocation table lies outside its header");
        if (count > 0 && tableEnd > bytes.Length) return Fail("truncated DOS MZ relocation table");
        if (windows is MzWindowsHeaderObservation.HeaderNeeded) return Fail("MZ Windows new-header pointer is outside the bounded prefix");
        var moduleBytes = imageBytes - headerBytes;
        for (var index = 0; index < count; index++)
        {
            var record = tableOffset + index * 4;
            if (reader.U16(record + 2) * 16L + reader.U16(record) + 2 > moduleBytes)
                return Fail($"DOS MZ relocation {index} points outside its load module");
        }
        var entryOffset = reader.U16(20);
        var entrySegment = reader.U16(22);
        if (entrySegment * 16L + entryOffset >= moduleBytes) return Fail("DOS MZ entry point lies outside its initialized load module");
        return new Result<DosMzHeader, string>.Success(new(headerBytes, imageBytes, moduleBytes,
            fileSize - imageBytes, count, tableOffset, entrySegment, entryOffset));
    }

    internal static Result<ExecutableMetadata, string> ParseExecutable(
        ReadOnlySpan<byte> bytes, long fileSize, ReadOnlySpan<byte> signature)
    {
        var windows = ObserveWindowsHeader(bytes, fileSize, signature);
        if (windows is MzWindowsHeaderObservation.HeaderNeeded or MzWindowsHeaderObservation.SignatureNeeded)
            return ExecutableHeader.Fail("Windows new-header pointer or signature is outside the bounded prefix");
        if (windows is MzWindowsHeaderObservation.Declared declared)
        {
            if (declared.Offset < 64 || declared.Offset > bytes.Length - 24L)
                return ExecutableHeader.Fail("invalid or truncated Windows executable header in MZ image");
            if (declared.Signature is "NE" or "LE" or "LX")
                return ExecutableHeader.Fail($"unsupported {declared.Signature} executable in MZ image");
            if (declared.Signature != "PE") return ExecutableHeader.Fail("invalid or truncated Windows executable header in MZ image");
            return PeHeader.ParseRecord(bytes[(int)declared.Offset..], PeHeader.ReadCodeView(bytes, (int)declared.Offset));
        }
        return Parse(bytes, fileSize, signature) switch
        {
            Result<DosMzHeader, string>.Success => ExecutableHeader.Ok(new SimpleExecutableMetadata("dos-mz", "x86")),
            Result<DosMzHeader, string>.Failure error => ExecutableHeader.Fail(error.Error),
            _ => throw new InvalidOperationException()
        };
    }
}
