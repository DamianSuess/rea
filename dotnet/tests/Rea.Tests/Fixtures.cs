using System.Buffers.Binary;
using System.Text;

namespace Rea.Tests;

internal static class Fixtures
{
    public static void U16(byte[] bytes, int offset, ushort value, bool little = true)
    {
        if (little) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
        else BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), value);
    }
    public static void U32(byte[] bytes, int offset, uint value, bool little = true)
    {
        if (little) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        else BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
    }
    public static void U64(byte[] bytes, int offset, ulong value, bool little = true)
    {
        if (little) BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);
        else BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(offset), value);
    }
    public static byte[] Elf(bool wide = true, bool little = true, ushort machine = 62)
    {
        var bytes = new byte[wide ? 64 : 52];
        "\u007fELF"u8.CopyTo(bytes); bytes[4] = (byte)(wide ? 2 : 1); bytes[5] = (byte)(little ? 1 : 2); bytes[6] = 1;
        U16(bytes, 18, machine, little); U32(bytes, 20, 1, little); U16(bytes, wide ? 52 : 40, (ushort)bytes.Length, little);
        return bytes;
    }
    public static byte[] ThinMach(bool wide = true, bool little = true, uint cpu = 0x01000007)
    {
        var bytes = new byte[wide ? 32 : 28];
        U32(bytes, 0, wide ? 0xfeedfacfu : 0xfeedfaceu, little); U32(bytes, 4, cpu, little); return bytes;
    }
    public static byte[] Fat(bool wide = false, bool little = false)
    {
        var entrySize = wide ? 32 : 20;
        var start = (8 + 2 * entrySize + 15) / 16 * 16;
        var bytes = new byte[start + 64];
        U32(bytes, 0, wide ? 0xcafebabfu : 0xcafebabeu, little); U32(bytes, 4, 2, little);
        for (var i = 0; i < 2; i++)
        {
            var entry = 8 + i * entrySize;
            var cpu = i == 0 ? 0x01000007u : 0x0100000cu;
            U32(bytes, entry, cpu, little);
            if (wide) { U64(bytes, entry + 8, (ulong)(start + i * 32), little); U64(bytes, entry + 16, 32, little); }
            else { U32(bytes, entry + 8, (uint)(start + i * 32), little); U32(bytes, entry + 12, 32, little); }
            U32(bytes, entry + (wide ? 24 : 16), 4, little);
            ThinMach(cpu: cpu).CopyTo(bytes, start + i * 32);
        }
        return bytes;
    }
    public static byte[] Pe(ushort machine = 0x8664, bool managed = false, ushort characteristics = 2, bool wide = true, bool codeView = false)
    {
        var optionalSize = wide ? 240 : 224;
        var bytes = new byte[codeView ? 1024 : 512];
        "MZ"u8.CopyTo(bytes); U32(bytes, 60, 64); "PE\0\0"u8.CopyTo(bytes.AsSpan(64));
        U16(bytes, 68, machine); U16(bytes, 84, (ushort)optionalSize); U16(bytes, 86, characteristics);
        U16(bytes, 88, (ushort)(wide ? 0x20b : 0x10b));
        var directory = 88 + (wide ? 112 : 96); U32(bytes, directory - 4, 16);
        if (managed) { U32(bytes, directory + 14 * 8, 0x2000); U32(bytes, directory + 14 * 8 + 4, 72); }
        if (codeView)
        {
            U16(bytes, 70, 1); U32(bytes, directory + 48, 0x1000); U32(bytes, directory + 52, 28);
            var section = 88 + optionalSize;
            U32(bytes, section + 12, 0x1000); U32(bytes, section + 16, 512); U32(bytes, section + 20, 512);
            var cv = CodeView(); U32(bytes, 512 + 12, 2); U32(bytes, 512 + 16, (uint)cv.Length);
            U32(bytes, 512 + 20, 0x101c); U32(bytes, 512 + 24, 540); cv.CopyTo(bytes, 540);
        }
        return bytes;
    }
    public static byte[] CodeView(bool rsds = true)
    {
        var offset = rsds ? 24 : 16;
        var path = Encoding.UTF8.GetBytes("C:\\caller\\private\\symbols.pdb\0");
        var bytes = new byte[offset + path.Length];
        (rsds ? "RSDS"u8 : "NB10"u8).CopyTo(bytes);
        if (rsds) new Guid("01234567-89ab-cdef-0123-456789abcdef").TryWriteBytes(bytes.AsSpan(4));
        U32(bytes, rsds ? 20 : 12, 7); path.CopyTo(bytes, offset); return bytes;
    }
    public static byte[] DosMz()
    {
        var bytes = new byte[160]; "MZ"u8.CopyTo(bytes); U16(bytes, 2, 160); U16(bytes, 4, 1); U16(bytes, 8, 2);
        U16(bytes, 24, 28); U32(bytes, 60, 0xfedcba98); return bytes;
    }
    public static byte[] JvmClass()
    {
        // Valid class with a large UTF8 constant, making its size exceed a plausible FAT table.
        using var stream = new MemoryStream();
        void Byte(byte b) => stream.WriteByte(b);
        void Word(ushort value) { Byte((byte)(value >> 8)); Byte((byte)value); }
        stream.Write([0xca, 0xfe, 0xba, 0xbe]); Word(0); Word(52); Word(3);
        Byte(1); Word(1200); stream.Write(Enumerable.Repeat((byte)'A', 1200).ToArray());
        Byte(7); Word(1); Word(1); Word(2); Word(0); Word(0); Word(0); Word(0); Word(0);
        return stream.ToArray();
    }
}
