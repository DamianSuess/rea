using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using System.Text;
using Rea.Domain.Managed;

namespace Rea.Managed;

/// <summary>The SDK owns table and heap decoding. This context owns projection bounds and file coordinates.</summary>
internal sealed class ManagedReadContext(ManagedPeImage image, MetadataReader metadata,
    ManagedStreamDeclaration? stringHeap, CancellationToken cancellation)
{
    public MetadataReader Metadata { get; } = metadata;
    public ManagedPeImage Image { get; } = image;
    public List<ManagedParseIssue> Issues { get; } = [];
    private long remaining = 64L * 1024 * 1024;
    private long transient;
    private readonly Dictionary<StringHandle, string> strings = [];
    private readonly Dictionary<EntityHandle, string?> typeNames = [];
    private readonly int stringsOffset = checked(image.Pe.PEHeaders.MetadataStartOffset + (stringHeap?.Offset ?? 0));
    private readonly int stringsSize = stringHeap?.Size ?? 0;

    public void Reserve(long bytes)
    {
        cancellation.ThrowIfCancellationRequested();
        if (bytes < 0 || bytes > remaining - transient) throw new ManagedCapacityException();
        remaining -= bytes;
    }

    public void AddIssue(string code, string scope, long? offset, string detail)
    {
        Reserve(256 + detail.Length * 6L + scope.Length * 6L);
        Issues.Add(new(code, scope, offset, detail));
    }

    public T? Read<T>(string scope, long? offset, Func<T> read) where T : class
    {
        Reserve(512);
        try { return read(); }
        catch (BadImageFormatException ex) { AddIssue("invalid-row", scope, offset, ex.Message); return null; }
        catch (DecoderFallbackException ex) { AddIssue("invalid-string", scope, offset, ex.Message); return null; }
        catch (ArgumentOutOfRangeException ex) { AddIssue("invalid-row", scope, offset, ex.Message); return null; }
        finally { transient = 0; }
    }

    public long RowOffset(TableIndex table, int row) => checked((long)Image.Pe.PEHeaders.MetadataStartOffset
        + Metadata.GetTableMetadataOffset(table) + (row - 1L) * Metadata.GetTableRowSize(table));

    public string String(StringHandle handle)
    {
        if (handle.IsNil) return "";
        if (strings.TryGetValue(handle, out var value))
        {
            // Repeated JSON occurrences still consume projection capacity.
            Reserve(value.Length * 6L + 2);
            return value;
        }
        var index = MetadataTokens.GetHeapOffset(handle);
        if (index < 0 || index >= stringsSize) throw new BadImageFormatException("String heap index is outside #Strings");
        var length = Image.Snapshot.Span.Slice(checked(stringsOffset + index), stringsSize - index).IndexOf((byte)0);
        if (length < 0) throw new BadImageFormatException("String heap item has no terminator within #Strings");
        Reserve(length * 8L + 64);
        value = Metadata.GetString(handle);
        strings.Add(handle, value);
        return value;
    }

    public byte[] Blob(BlobHandle handle)
    {
        if (handle.IsNil) return [];
        var reader = Metadata.GetBlobReader(handle);
        cancellation.ThrowIfCancellationRequested();
        if (reader.Length > remaining - transient) throw new ManagedCapacityException();
        transient += reader.Length;
        Reserve(128);
        return reader.ReadBytes(reader.Length);
    }

    public string? Guid(GuidHandle handle) => handle.IsNil ? null : Metadata.GetGuid(handle).ToString();
    public static string Token(EntityHandle handle) => $"0x{MetadataTokens.GetToken(handle):x8}";
    public static string Token(TableIndex table, int row) => $"0x{((int)table << 24 | row):x8}";
    public static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public string? TypeName(EntityHandle handle)
    {
        if (handle.IsNil) return null;
        if (typeNames.TryGetValue(handle, out var cached)) { Reserve((cached?.Length ?? 0) * 6L + 2); return cached; }
        string? value;
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                var def = Metadata.GetTypeDefinition((TypeDefinitionHandle)handle);
                value = Qualified(String(def.Namespace), String(def.Name)); break;
            case HandleKind.TypeReference:
                var reference = Metadata.GetTypeReference((TypeReferenceHandle)handle);
                value = Qualified(String(reference.Namespace), String(reference.Name)); break;
            case HandleKind.MethodDefinition:
                value = TypeName(Metadata.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType()); break;
            case HandleKind.MemberReference:
                // A MemberRef parent may itself be a MethodDef for a vararg call site.
                var parent = Metadata.GetMemberReference((MemberReferenceHandle)handle).Parent;
                value = parent.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.MethodDefinition ? TypeName(parent) : null;
                break;
            default: value = null; break;
        }
        Reserve(128 + (value?.Length ?? 0) * 6L);
        typeNames.Add(handle, value);
        return value;
    }

    private static string Qualified(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;
}

internal sealed class ManagedCapacityException : Exception
{
    public ManagedCapacityException() : base("Managed projection exceeds its 67108864-byte representation budget") { }
}
