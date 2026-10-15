using Rea.Domain;
using Rea.Domain.Managed;

namespace Rea.Application;

/// <summary>Provider boundary accepts one owned file snapshot; target code must never be loaded.</summary>
public interface IManagedArtifactInspector
{
    Result<ManagedArtifactInspection, InspectionError> Inspect(
        ManagedArtifactSnapshot snapshot, CancellationToken cancellationToken);
}
