using System.Buffers.Binary;
using System.Text;

namespace Rea.Managed;

internal sealed record ManagedStreamDeclaration(string Name, int Offset, int Size);

// The SDK exposes decoded tables, not the declared stream-name directory.
// This parser owns only those directory facts; row and heap interpretation stays with the SDK.
internal static class ManagedStreamHeaders
{
    public static IReadOnlyList<ManagedStreamDeclaration> Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x424a5342)
            throw new BadImageFormatException("Invalid CLI metadata root signature");
        var versionLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
        var start = (16L + versionLength + 3) & ~3L;
        if (start > bytes.Length - 4) throw new BadImageFormatException("Metadata version extends beyond its directory");
        var offset = (int)start;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offset + 2)..]); offset += 4;
        if (count > (bytes.Length - offset) / 12) throw new BadImageFormatException("Metadata stream count exceeds directory bytes");
        var declarations = new List<ManagedStreamDeclaration>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            if (offset > bytes.Length - 12) throw new BadImageFormatException("Truncated metadata stream header");
            var streamOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
            var streamSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]);
            if (streamOffset > bytes.Length || streamSize > bytes.Length - streamOffset)
                throw new BadImageFormatException("Metadata stream range extends beyond its directory");
            offset += 8;
            var terminator = bytes.Slice(offset, Math.Min(32, bytes.Length - offset)).IndexOf((byte)0);
            if (terminator < 0) throw new BadImageFormatException("Metadata stream name is not terminated within 32 bytes");
            var encodedName = bytes.Slice(offset, terminator);
            foreach (var character in encodedName)
                if (character > 127) throw new BadImageFormatException("Metadata stream name is not ASCII");
            var name = Encoding.ASCII.GetString(encodedName);
            if (!seen.Add(name)) throw new BadImageFormatException("Duplicate metadata stream name");
            declarations.Add(new(name, (int)streamOffset, (int)streamSize)); offset += (terminator + 1 + 3) & ~3;
        }
        return declarations.AsReadOnly();
    }
}
