using System.Buffers.Binary;

namespace Rea.Domain;

// Format owners validate extents before reading. This reader only owns byte order.
internal readonly ref struct EndianReader
{
    private readonly ReadOnlySpan<byte> bytes;
    private readonly bool little;

    public EndianReader(ReadOnlySpan<byte> bytes, bool little = true)
    {
        this.bytes = bytes;
        this.little = little;
    }
    public ushort U16(int offset) => little
        ? BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..])
        : BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
    public uint U32(int offset) => little
        ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..])
        : BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
    public ulong U64(int offset) => little
        ? BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..])
        : BinaryPrimitives.ReadUInt64BigEndian(bytes[offset..]);
}
