using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using Rea.Domain.Managed;

namespace Rea.Managed;

internal sealed class ManagedInventory(ManagedReadContext context)
{
    private MetadataReader Metadata => context.Metadata;
    public List<ManagedAssemblyReference> AdmittedReferences { get; } = [];
    public List<ManagedResource> AdmittedResources { get; } = [];
    public List<ManagedCustomAttribute> AdmittedAttributes { get; } = [];

    public ManagedModuleIdentity? Module() => context.Read("metadata.Module", Offset(TableIndex.Module, 1), () =>
    {
        if (Metadata.GetTableRowCount(TableIndex.Module) != 1) throw new BadImageFormatException("Module table must contain exactly one row");
        var module = Metadata.GetModuleDefinition();
        return new ManagedModuleIdentity(context.String(module.Name), module.Generation, context.Guid(module.Mvid),
            context.Guid(module.GenerationId), context.Guid(module.BaseGenerationId), "0x00000001", Offset(TableIndex.Module, 1));
    });

    public ManagedAssemblyIdentity? Assembly()
    {
        if (!Metadata.IsAssembly) return null;
        return context.Read("metadata.Assembly", Offset(TableIndex.Assembly, 1), () =>
        {
            if (Metadata.GetTableRowCount(TableIndex.Assembly) != 1) throw new BadImageFormatException("Assembly table has an invalid row count");
            var value = Metadata.GetAssemblyDefinition();
            return new ManagedAssemblyIdentity(context.String(value.Name), value.Version.ToString(), Culture(value.Culture),
                (uint)value.Flags, (uint)value.HashAlgorithm, Key(value.PublicKey, true), "0x20000001", Offset(TableIndex.Assembly, 1));
        });
    }

    public IReadOnlyList<ManagedAssemblyReference> References()
    {
        var result = AdmittedReferences;
        foreach (var handle in Metadata.AssemblyReferences)
        {
            var row = MetadataTokens.GetRowNumber(handle);
            var item = context.Read("metadata.AssemblyRef", Offset(TableIndex.AssemblyRef, row), () =>
            {
                var value = Metadata.GetAssemblyReference(handle); var hash = context.Blob(value.HashValue);
                return new ManagedAssemblyReference(context.String(value.Name), value.Version.ToString(), Culture(value.Culture),
                    (uint)value.Flags, Key(value.PublicKeyOrToken, (value.Flags & AssemblyFlags.PublicKey) != 0),
                    hash.Length == 0 ? null : ManagedReadContext.Digest(hash), hash.Length, ManagedReadContext.Token(handle), Offset(TableIndex.AssemblyRef, row));
            });
            if (item is not null) result.Add(item);
        }
        return result.AsReadOnly();
    }

    public IReadOnlyList<ManagedResource> Resources()
    {
        var result = AdmittedResources;
        foreach (var handle in Metadata.ManifestResources)
        {
            var row = MetadataTokens.GetRowNumber(handle);
            var item = context.Read("metadata.ManifestResource", Offset(TableIndex.ManifestResource, row), () =>
            {
                var value = Metadata.GetManifestResource(handle); var embedded = value.Implementation.IsNil;
                int? size = null; string? digest = null;
                if (embedded)
                {
                    try
                    {
                        var directory = context.Image.Pe.PEHeaders.CorHeader!.ResourcesDirectory;
                        var offset = context.Image.FileOffset(directory.RelativeVirtualAddress, directory.Size);
                        if (value.Offset < 0 || value.Offset > directory.Size - 4L) throw new BadImageFormatException("Embedded resource length is outside the CLI resource directory");
                        var start = checked((int)(offset + value.Offset));
                        var length = BinaryPrimitives.ReadUInt32LittleEndian(context.Image.Snapshot.Span[start..]);
                        if (length > directory.Size - value.Offset - 4) throw new BadImageFormatException("Embedded resource payload extends beyond the CLI resource directory");
                        size = checked((int)length); digest = ManagedReadContext.Digest(context.Image.Snapshot.Span.Slice(start + 4, size.Value));
                    }
                    catch (BadImageFormatException ex) { context.AddIssue("invalid-resource", "metadata.ManifestResource", Offset(TableIndex.ManifestResource, row), ex.Message); }
                }
                var flags = (uint)value.Attributes;
                return new ManagedResource(context.String(value.Name), flags, (flags & 7) switch { 1 => "public", 2 => "private", _ => "unknown" },
                    embedded ? null : ManagedReadContext.Token(value.Implementation), embedded, checked((uint)value.Offset), size, digest,
                    ManagedReadContext.Token(handle), Offset(TableIndex.ManifestResource, row));
            });
            if (item is not null) result.Add(item);
        }
        return result.AsReadOnly();
    }

    public IReadOnlyList<ManagedCustomAttribute> Attributes()
    {
        var result = AdmittedAttributes;
        foreach (var handle in Metadata.CustomAttributes)
        {
            var row = MetadataTokens.GetRowNumber(handle);
            var item = context.Read("metadata.CustomAttribute", Offset(TableIndex.CustomAttribute, row), () =>
            {
                var value = Metadata.GetCustomAttribute(handle); var blob = context.Blob(value.Value);
                var typeName = context.TypeName(value.Constructor);
                string? decoded = null;
                if (typeName == "System.Runtime.Versioning.TargetFrameworkAttribute")
                {
                    try
                    {
                        var reader = Metadata.GetBlobReader(value.Value);
                        if (reader.ReadUInt16() != 1) throw new BadImageFormatException("Invalid custom-attribute prolog");
                        // Only the known TargetFrameworkAttribute fixed-string constructor is interpreted.
                        if (SingleStringConstructor(value.Constructor))
                        {
                            context.Reserve(reader.RemainingBytes * 8L + 2);
                            decoded = reader.ReadSerializedString();
                        }
                    }
                    catch (BadImageFormatException ex) { context.AddIssue("invalid-blob", "metadata.CustomAttribute", Offset(TableIndex.CustomAttribute, row), ex.Message); }
                }
                return new ManagedCustomAttribute(ManagedReadContext.Token(value.Parent), value.Constructor.IsNil ? null : ManagedReadContext.Token(value.Constructor),
                    typeName, blob.Length, ManagedReadContext.Digest(blob), decoded, ManagedReadContext.Token(handle), Offset(TableIndex.CustomAttribute, row));
            });
            if (item is not null) result.Add(item);
        }
        return result.AsReadOnly();
    }

    private bool SingleStringConstructor(EntityHandle constructor)
    {
        BlobHandle signature;
        StringHandle name;
        if (constructor.Kind == HandleKind.MemberReference)
        { var member = Metadata.GetMemberReference((MemberReferenceHandle)constructor); signature = member.Signature; name = member.Name; }
        else if (constructor.Kind == HandleKind.MethodDefinition)
        { var method = Metadata.GetMethodDefinition((MethodDefinitionHandle)constructor); signature = method.Signature; name = method.Name; }
        else return false;
        if (context.String(name) != ".ctor") return false;
        var bytes = context.Blob(signature);
        return bytes.AsSpan().SequenceEqual(new byte[] { 0x20, 0x01, 0x01, 0x0e });
    }

    private ManagedPublicKey Key(BlobHandle handle, bool full)
    {
        var bytes = context.Blob(handle);
        if (bytes.Length == 0) return new("none", 0, null, null);
        string? token = null;
        if (full)
        {
            var hash = SHA1.HashData(bytes); Array.Reverse(hash, hash.Length - 8, 8);
            token = Convert.ToHexStringLower(hash.AsSpan(hash.Length - 8));
        }
        else if (bytes.Length == 8) token = Convert.ToHexStringLower(bytes);
        return new(full ? "public-key" : "public-key-token", bytes.Length, ManagedReadContext.Digest(bytes), token);
    }
    private string? Culture(StringHandle handle) { var value = context.String(handle); return value.Length == 0 ? null : value; }
    private long Offset(TableIndex table, int row) => context.RowOffset(table, row);
}
