using Rea.Domain;
using Rea.Domain.Managed;

namespace Rea.Application;

/// <summary>Snapshot and file identity have one owner shared by all managed facets.</summary>
public sealed class ManagedInspectionService(IManagedArtifactInspector inspector)
{
    public async Task<Result<ManagedArtifactInspection, InspectionError>> InspectAsync(
        string callerPath, int maxFileBytes = 64 * 1024 * 1024, CancellationToken cancellationToken = default)
    {
        Result<ManagedArtifactInspection, InspectionError> Fail(string code, string message) =>
            new Result<ManagedArtifactInspection, InspectionError>.Failure(new(code, message));
        if (string.IsNullOrWhiteSpace(callerPath) || callerPath.Contains('\0')) return Fail("invalid-input", "A nonempty, NUL-free local path is required");
        if (maxFileBytes <= 0 || maxFileBytes > Array.MaxLength) return Fail("invalid-input", "File budget must fit a positive CLR byte-array length");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(callerPath);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var size = stream.Length;
            if (size > maxFileBytes) return Fail("resource-limit", $"File size {size} exceeds the caller-selected snapshot budget {maxFileBytes}");
            var snapshot = new byte[(int)size];
            var count = await stream.ReadAtLeastAsync(snapshot, snapshot.Length, false, cancellationToken);
            if (count != snapshot.Length || stream.Length != size) return Fail("target-changed", "File length changed while taking the managed snapshot");
            cancellationToken.ThrowIfCancellationRequested();
            return inspector.Inspect(ManagedArtifactSnapshot.TakeOwnership(callerPath, snapshot), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Fail("cancelled", "Managed inspection was cancelled"); }
        catch (UnauthorizedAccessException ex) { return Fail("access-denied", ex.Message); }
        catch (FileNotFoundException ex) { return Fail("not-found", ex.Message); }
        catch (DirectoryNotFoundException ex) { return Fail("not-found", ex.Message); }
        catch (IOException ex) { return Fail("io-error", ex.Message); }
        catch (ArgumentException ex) { return Fail("invalid-input", ex.Message); }
        catch (NotSupportedException ex) { return Fail("invalid-input", ex.Message); }
    }
}
