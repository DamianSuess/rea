using System.Reflection.PortableExecutable;

namespace Rea.Managed;

/// <summary>Owns the mapping between SDK PE declarations and actual retained file bytes.</summary>
internal sealed class ManagedPeImage(PEReader pe, ReadOnlyMemory<byte> snapshot)
{
    public PEReader Pe { get; } = pe;
    public ReadOnlyMemory<byte> Snapshot { get; } = snapshot;

    public long FileOffset(int rva, int length)
    {
        if (rva < 0 || length < 0) throw new BadImageFormatException("Negative PE RVA or length");
        var headers = Pe.PEHeaders;
        var headerSize = headers.PEHeader!.SizeOfHeaders;
        if (rva < headerSize && length <= headerSize - rva && length <= Snapshot.Length - (long)rva) return rva;
        foreach (var section in headers.SectionHeaders)
        {
            var relative = (long)rva - section.VirtualAddress;
            var offset = section.PointerToRawData + relative;
            if (relative >= 0 && relative < section.SizeOfRawData && length <= section.SizeOfRawData - relative
                && section.PointerToRawData > 0 && offset <= Snapshot.Length && length <= Snapshot.Length - offset) return offset;
        }
        throw new BadImageFormatException("PE directory is not fully backed by file bytes");
    }
}
