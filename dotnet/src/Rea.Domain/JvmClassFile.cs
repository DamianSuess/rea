using System.Buffers.Binary;

namespace Rea.Domain;

// CAFEBABE identifies both JVM classes and FAT Mach-O; validate the complete class framing.
internal static class JvmClassFile
{
    private ref struct Cursor
    {
        private readonly ReadOnlySpan<byte> bytes;
        private int offset;
        public Cursor(ReadOnlySpan<byte> bytes) { this.bytes = bytes; offset = 0; }
        public readonly bool AtEnd => offset == bytes.Length;
        public int U1() => offset < bytes.Length ? bytes[offset++] : -1;
        public int U2()
        {
            if (offset > bytes.Length - 2) return -1;
            var value = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]); offset += 2; return value;
        }
        public long U4()
        {
            if (offset > bytes.Length - 4) return -1;
            var value = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]); offset += 4; return value;
        }
        public bool Skip(long count)
        {
            if (count < 0 || count > bytes.Length - offset) return false;
            offset += (int)count; return true;
        }
    }

    internal static bool IsClassFile(ReadOnlySpan<byte> bytes)
    {
        var cursor = new Cursor(bytes);
        if (cursor.U4() != 0xcafebabe || cursor.U2() < 0) return false;
        var major = cursor.U2(); var count = cursor.U2();
        if (major < 45 || count <= 0) return false;
        // Even the smallest constant needs three bytes per occupied slot.
        // Reject impossible counts before expanding three parallel tables.
        if (count - 1 > (bytes.Length - 10) / 3) return false;
        // count is a u16 field, bounding these tables independently of input length.
        var tags = new int[count]; var first = new int[count]; var second = new int[count];
        bool Has(int index, params ReadOnlySpan<int> expected) => index > 0 && index < count && expected.Contains(tags[index]);
        for (var i = 1; i < count; i++)
        {
            var tag = cursor.U1(); tags[i] = tag;
            switch (tag)
            {
                case 1: if (!cursor.Skip(cursor.U2())) return false; break;
                case 3: case 4: if (cursor.U4() < 0) return false; break;
                case 5: case 6: if (i + 1 >= count || !cursor.Skip(8)) return false; i++; break;
                case 7: case 8: case 16: case 19: case 20:
                    first[i] = cursor.U2(); if (first[i] < 0) return false; break;
                case 9: case 10: case 11: case 12: case 17: case 18:
                    first[i] = cursor.U2(); second[i] = cursor.U2(); if (first[i] < 0 || second[i] < 0) return false; break;
                case 15: first[i] = cursor.U1(); second[i] = cursor.U2(); if (first[i] < 0 || second[i] < 0) return false; break;
                default: return false;
            }
        }
        for (var i = 1; i < count; i++)
        {
            var a = first[i]; var b = second[i];
            switch (tags[i])
            {
                case 7: case 8: case 16: case 19: case 20:
                    if (!Has(a, 1) || (tags[i] == 16 && major < 51) || (tags[i] is 19 or 20 && major < 53)) return false;
                    break;
                case 9: case 10: case 11: if (!Has(a, 7) || !Has(b, 12)) return false; break;
                case 12: if (!Has(a, 1) || !Has(b, 1)) return false; break;
                case 15:
                    if (a is < 1 or > 9 || major < 51) return false;
                    var valid = a switch { <= 4 => Has(b, 9), 5 or 8 => Has(b, 10), 6 or 7 => major >= 52 ? Has(b, 10, 11) : Has(b, 10), _ => Has(b, 11) };
                    if (!valid) return false;
                    break;
                case 17: case 18: if (!Has(b, 12) || major < (tags[i] == 17 ? 55 : 51)) return false; break;
            }
        }
        if (cursor.U2() < 0 || !Has(cursor.U2(), 7)) return false;
        var superClass = cursor.U2();
        if (superClass < 0 || (superClass != 0 && !Has(superClass, 7))) return false;
        var interfaces = cursor.U2();
        if (interfaces < 0 || !cursor.Skip(interfaces * 2L)) return false;
        if (!Members(ref cursor, tags) || !Members(ref cursor, tags)) return false;
        return Attributes(ref cursor, tags, cursor.U2()) && cursor.AtEnd;
    }

    private static bool Attributes(ref Cursor cursor, int[] tags, int count)
    {
        if (count < 0) return false;
        for (var i = 0; i < count; i++)
        {
            var name = cursor.U2(); var length = cursor.U4();
            if (!Utf8(tags, name) || !cursor.Skip(length)) return false;
        }
        return true;
    }
    private static bool Utf8(int[] tags, int index) => index > 0 && index < tags.Length && tags[index] == 1;
    private static bool Members(ref Cursor cursor, int[] tags)
    {
        var count = cursor.U2(); if (count < 0) return false;
        for (var i = 0; i < count; i++)
            if (cursor.U2() < 0 || !Utf8(tags, cursor.U2()) || !Utf8(tags, cursor.U2()) || !Attributes(ref cursor, tags, cursor.U2())) return false;
        return true;
    }
}
