using System.Security.Cryptography;
using Rea.Domain.Managed;

namespace Rea.Application;

/// <summary>An owned snapshot binds content and identity once, before provider dispatch.</summary>
public sealed class ManagedArtifactSnapshot
{
    private readonly byte[] bytes;
    public ManagedArtifactIdentity Identity { get; }
    public ReadOnlyMemory<byte> Content => bytes;

    private ManagedArtifactSnapshot(string callerPath, byte[] ownedBytes)
    {
        bytes = ownedBytes;
        Identity = new(callerPath, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length);
    }

    // External callers retain ownership of their inputs; mutations cannot alter this snapshot.
    public static ManagedArtifactSnapshot Create(string callerPath, ReadOnlySpan<byte> bytes) => new(callerPath, bytes.ToArray());

    // Only the application file owner transfers its otherwise unexposed read buffer.
    internal static ManagedArtifactSnapshot TakeOwnership(string callerPath, byte[] ownedBytes) => new(callerPath, ownedBytes);

    public Stream OpenRead() => new MemoryStream(bytes, false);
}
