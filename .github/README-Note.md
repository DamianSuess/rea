# Restored repository automation

The original workflow, composite-action, issue-template and PR-template paths
are retained. Executable orchestration now uses .NET 10 and Rea.Automation,
with no authored JavaScript or Node runtime dependency.

See [current workflow status](../docs/dotnet-workflows.md) and
`workflow-compatibility.json`. Provider checks without a current C# adapter fail
explicitly as unavailable. Release qualification prepares and verifies artifacts;
publication and recovery remain gated. PR readiness retains human declarations
and executes only trusted default-branch automation.

The restored issue forms support early proposals and historical provider reports;
selecting an area does not establish current support. No workflows, branch
protection settings, live status updates or publications were run remotely as
part of the local adaptation.
