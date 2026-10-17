# REA-SHARP — C# / .NET 10

_Reverse Engineer Anything **SHARP**_ is a complete rewrite of the original REA in C# to remove Node/NPM and JavaScript overhead, improving performance.

Install from this checkout with the [C# installer](docs/csharp-installation.md).

REA is a local, read-only executable and managed-assembly inspection tool.
The application, automation, experiment runner, tests and fixtures use C#.
Building the .NET solution requires only the .NET 10 SDK, with no Node/npm or
TypeScript/JavaScript runtime. Restored engine-required Python, Java and Swift
sources in [bridge](bridge/README.md) are the explicitly approved exception;
they require their external engines and are not active C# providers. Configuration,
documentation and image assets remain in their appropriate formats.

**This is a breaking cutover to a smaller application, not a complete
feature-equivalent translation of the previous REA implementation.** The legacy
runtime and npm packaging remain removed. The docs, agent guides, GitHub workflows,
bridge sources and experiment records have been restored and adapted; historical
examples are labelled. The available analysis commands are
`inspect-header` and `inspect-managed-artifact`.

## Build, test, and run

Run these commands from the repository root:

```text
dotnet build Rea.slnx --configuration Release
dotnet run --project dotnet/tests/Rea.Tests --configuration Release --no-build
dotnet run --project dotnet/src/Rea.Cli --configuration Release --no-build -- --help
dotnet run --project dotnet/src/Rea.Cli --configuration Release --no-build -- inspect-header /path/to/binary
dotnet run --project dotnet/src/Rea.Cli --configuration Release --no-build -- inspect-managed-artifact /path/to/assembly.dll
```

On Windows it will install to: `C:\Users\USERNAME\.local\share\rea\dotnet-tool\`

Execute it via:

```text
C:\Users\USERNAME\.local\share\rea\dotnet-tool\rea.exe" --help
```

The dependency-free test executable runs the actual assertions; `dotnet test`
is not this repository's verification command. Tests audit the checkout to reject
foreign application/orchestration code and legacy manifests, and verify the
approved bridge-source receipts. See the [C# validation gates](scripts/README.md)
for discovery, architecture, documentation, skill, cleanup and installed-package checks.

## Supported inspection

- ELF, PE, thin/universal Mach-O, DOS MZ, and explicitly selected DOS COM headers.
- Architecture declarations, PE executable roles, CLI declarations, and CodeView
  symbol references with bounds and malformed-input checks.
- Managed module and assembly identity, references, embedded-resource hashes,
  custom-attribute identities, target-framework declarations, metadata table
  counts, and source row offsets.
- Derived managed architecture and runtime classification, with observations,
  limitations, unknowns, cancellation, and memory budgets reported explicitly.

Neither command executes a target or loads its managed assembly. Results are
JSON. The header command hashes its retained prefix; the managed command hashes
its owned full-file snapshot. Neither establishes an atomic filesystem snapshot.
See [implementation details](dotnet/README.md) and [architecture](docs/architecture.mermaid).

## Removed functionality

The former MCP server, full command catalog, Evidence/session workflows, deep
analysis providers, debugging/instrumentation, browser and Electron workflows,
artifact extraction, active platform-specific adapters, provider setup, the former website runtime, and
npm distribution are unavailable in this checkout. Managed members, signatures,
IL, and native-boundary analysis also remain unavailable. They must be implemented
and verified in C# before being advertised again. Engine-side JavaScript agents
and generated JavaScript scripts are excluded.

The previous committed implementation is recoverable from Git history at
`ef250b14442ad99458ef7ba1e59b78c809238675`. No legacy source archive is shipped
inside the application package. Restored bridge code remains separately under
`bridge/`. `CHANGELOG.md` records historical releases and does not
describe the current supported feature set.

## Package locally

```text
dotnet pack dotnet/src/Rea.Cli --configuration Release --no-build --output dotnet/artifacts/packages
dotnet exec dotnet/tests/Rea.Tests/bin/Release/net10.0/Rea.Tests.dll --verify-packages dotnet/artifacts/packages
```

This produces the local `Rea.DotNet.Preview` .NET tool package, with command `rea`.
It does not install or publish it. Package tests verify .NET dependencies and
reject JavaScript/TypeScript, Node bindings, and bundled test fixtures.

See [contribution guidelines](CONTRIBUTING.md), [security](SECURITY.md),
[responsible use](DISCLAIMER.md), and [license](LICENSE).

## Restored development workflows

See [current C# automation and restoration status](docs/dotnet-workflows.md) for
PR checks, documentation generation, qualified packages and experiment commands.
Original provider workflow filenames remain as manual availability gates, which
fail explicitly instead of claiming verification. Release qualification produces
source-bound .NET artifacts; publication remains gated.
