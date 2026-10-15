# C# experiment entry point

Build the root .NET solution, then use Rea.Experiments `measure` and `compare`.
See [the current guide](../../docs/dotnet-workflows.md#experiments) for exact commands,
metrics, output ownership and limitations.

The restored reports, corpus JSON and question files retain their original
evidence and attribution. They are not current verification. The 33 former
Node/Python entry points are mapped in `entrypoint-migration.json` with the
original source hashes. The new runner measures the two supported C# commands;
provider/MCP/model-specific behavior remains unavailable rather than simulated.
