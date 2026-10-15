using System.Security.Cryptography;
using Rea.Domain;

namespace Rea.Application;

public sealed record InspectionError(string Code, string Message);
public sealed record HeaderInspectionRequest(string Path, string HostArchitecture, int HeaderBytes = 65536, string? FormatHint = null);
public sealed record RetainedHeaderSource(string CallerPath, string AbsolutePath, long FileBytes, int Offset, int RetainedBytes, string Sha256);
public sealed record HeaderInspection(RetainedHeaderSource Source, ExecutableMetadata Header, IReadOnlyList<string> Limitations);

/// <summary>Owns the file handle and retained prefix. Parsers never reopen or reinterpret a caller's path.</summary>
public sealed class HeaderInspectionService
{
    public async Task<Result<HeaderInspection, InspectionError>> InspectAsync(
        HeaderInspectionRequest request, CancellationToken cancellationToken = default)
    {
        Result<HeaderInspection, InspectionError> Fail(string code, string message) =>
            new Result<HeaderInspection, InspectionError>.Failure(new(code, message));
        if (string.IsNullOrWhiteSpace(request.Path) || request.Path.Contains('\0')) return Fail("invalid-input", "A nonempty, NUL-free local path is required");
        if (request.HeaderBytes <= 0 || request.HeaderBytes > Array.MaxLength) return Fail("invalid-input", "Header bytes must fit a positive CLR byte-array length");
        if (request.FormatHint is not (null or "dos-com")) return Fail("invalid-input", "Supported explicit format: dos-com");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = System.IO.Path.GetFullPath(request.Path);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var fileBytes = stream.Length;
            var prefix = new byte[(int)Math.Min(fileBytes, request.HeaderBytes)];
            var read = await stream.ReadAtLeastAsync(prefix, prefix.Length, false, cancellationToken);
            if (read != prefix.Length || stream.Length != fileBytes) return Fail("target-changed", "File length changed while reading its header");
            var parsed = request.FormatHint == "dos-com" ? ExecutableHeader.ParseDosCom(fileBytes)
                : ExecutableHeader.Parse(prefix, request.HostArchitecture, fileBytes);
            if (parsed is Result<ExecutableMetadata, string>.Failure error) return Fail("invalid-header", error.Error);
            var metadata = ((Result<ExecutableMetadata, string>.Success)parsed).Value;
            var limitations = new List<string>
            {
                "Only executable header declarations were inspected; this does not establish provider or runtime support.",
                "The digest identifies retained prefix bytes, not the complete file or an atomic filesystem snapshot."
            };
            if (prefix.Length < fileBytes)
                limitations.Add("Bytes beyond the retained prefix were not examined; omitted debug or ABI metadata remains unknown.");
            if (request.FormatHint == "dos-com")
                limitations.Add("DOS COM is a caller-selected interpretation; no identifying signature establishes that format.");
            return new Result<HeaderInspection, InspectionError>.Success(new(
                new(request.Path, path, fileBytes, 0, prefix.Length, Convert.ToHexStringLower(SHA256.HashData(prefix))),
                metadata, limitations.AsReadOnly()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Fail("cancelled", "Header inspection was cancelled"); }
        catch (UnauthorizedAccessException ex) { return Fail("access-denied", ex.Message); }
        catch (FileNotFoundException ex) { return Fail("not-found", ex.Message); }
        catch (DirectoryNotFoundException ex) { return Fail("not-found", ex.Message); }
        catch (IOException ex) { return Fail("io-error", ex.Message); }
        catch (ArgumentException ex) { return Fail("invalid-input", ex.Message); }
        catch (NotSupportedException ex) { return Fail("invalid-input", ex.Message); }
    }
}
