# REA on C# and .NET 10

This is the implementation guide for the C# application. It builds, runs, tests, and
packages with the .NET 10 SDK alone. There are no Node, npm, TypeScript, or NuGet
package dependencies in this solution.

**Feature parity with the former implementation has not been achieved.**
The former runtime remains removed. Supporting docs, agent guides, workflows,
engine-required bridge sources and experiment records have been restored and
adapted; see [current workflows](../docs/dotnet-workflows.md). This application exposes
executable-header and managed-assembly inspection. It does not delegate any operation to that
implementation, and it does not advertise unported tools as available.

The implementation policy is C# only: no TypeScript, JavaScript, Node runtime,
JavaScript bindings, or generated engine-side scripts in the replacement.
Engine-required Python, Java and Swift source under `bridge/` is the user's
explicit exception. It is not bundled in the C# package or enabled by this app.
Frida JavaScript agents, active Electron injection hooks, and browser operations
that require evaluating JavaScript are excluded from the replacement. Compatible
static inspection and protocol operations still require C# ports; this policy
does not classify an unported feature as incompatible. Unsupported features are
unavailable after this cutover. Their former source is recoverable from Git
history at `ef250b14442ad99458ef7ba1e59b78c809238675`.

## Build and run

Run from the repository root:

```sh
dotnet build Rea.slnx --configuration Release
dotnet run --project dotnet/src/Rea.Cli --configuration Release --no-build -- --help
dotnet run --project dotnet/src/Rea.Cli --configuration Release --no-build -- inspect-header /path/to/binary
dotnet run --project dotnet/src/Rea.Cli --configuration Release --no-build -- inspect-managed-artifact /path/to/assembly.dll
dotnet run --project dotnet/tests/Rea.Tests --configuration Release --no-build
```

The test project is a dependency-free executable, not a VSTest project. Execute
the last command to run its assertions; `dotnet test` is not the verification
command. The runner exits unsuccessfully on any failing assertion or if no tests
are discovered. It includes a real CLI subprocess inspecting the built managed
assembly and deterministic malformed-input and truncation checks.

For direct invocation after a build:

```sh
dotnet exec dotnet/src/Rea.Cli/bin/Release/net10.0/rea.dll inspect-header /path/to/binary
dotnet exec dotnet/tests/Rea.Tests/bin/Release/net10.0/Rea.Tests.dll
```

Options follow the path:

| Option | Meaning |
| --- | --- |
| `--host-architecture x64` | Select a FAT Mach-O slice using `x64`, `arm64`, `ia32`, or `arm`; defaults to the process architecture |
| `--header-bytes 65536` | Caller-selected retained-prefix memory budget; must fit a positive CLR byte-array length |
| `--format dos-com` | Explicit headerless DOS COM interpretation, limited to 1–65,280 file bytes |

Normal output is JSON. Exit codes are 0 for success, 1 for inspection failure,
2 for invalid command syntax, and 130 for cancellation. Ctrl+C cancels the active
read. Duplicate or unknown options are rejected. Reading a file never executes
it, attaches a debugger, or changes its bytes.

`inspect-managed-artifact PATH [--max-file-bytes N]` takes an owned full-file
snapshot, bounded by the caller's file budget (default 64 MiB), and returns the
static inventory using the existing managed contract's snake_case field names.
Null values preserve explicit unknowns. An independent 64 MiB representation
budget bounds eager inventory decoding and JSON expansion; transient blobs are
admitted before allocation and released from that budget after each row.
Exceeding that budget reports partial coverage with admitted facts retained.
The C# result extends the legacy managed shape by permitting a null
`ready_to_run_signature` when a declared native header cannot be mapped. That
uncertainty is retained instead of classifying an unexamined signature as absent.

## Implemented scope

| Component | C# implementation |
| --- | --- |
| Expected failures | Closed `Result<T, TError>` variants |
| ELF headers | 32/64-bit, both byte orders, version and extent validation; x86, x86-64, ARM, ARM64, MIPS, PowerPC, RISC-V; raw family declarations retained |
| PE headers | Original COFF machine, executable role, CLI-directory declaration, ReadyToRun OS overrides, optional-header and directory extents |
| PE debug metadata | RSDS/NB10 CodeView paths, GUID/age, raw-file and RVA mapping, bounds checks |
| Mach-O headers | Thin 32/64-bit in both byte orders; load-command extent checks |
| Universal Mach-O | FAT32/FAT64 in both byte orders; all slice ranges and alignments checked before deterministic host selection; JVM class disambiguation |
| DOS MZ | Module/overlay coordinates, relocation and entry-point checks; short DOS headers distinguished from Windows new-header declarations |
| Header workflow | Owned read-only file handle, bounded prefix, cancellation, typed filesystem failures, source path and retained-prefix SHA-256 |
| Managed inventory | SDK metadata decoding without `Assembly.Load`; module/MVID, assembly identity and public keys, references, resources, attributes, TargetFramework declarations, table counts and row offsets |
| Managed classification | PE/CLI architecture and flags, AnyCPU/preferred-32 semantics, observed ReadyToRun signature, derived runtime family with supporting facts |
| Managed workflow | Owned full-file snapshot and SHA-256 computed once, caller-selected input retained, cancellation, budget checks before allocation, partial facts retained on damaged records |
| CLI | `inspect-header`, `inspect-managed-artifact`, help and preview version |

The managed adapter uses `System.Reflection.Metadata` from .NET 10 rather than
reimplementing metadata table layouts. `ManagedPeImage` owns file-backed RVA
mapping, `ManagedReadContext` owns decoded string identity and capacity, and
`ManagedArtifactSnapshot` owns content and its full-file identity. Resource bytes
are hashed directly from that snapshot. Stream-name declarations use one narrow
format parser because the SDK does not expose that directory.

The former parser source counterparts were `src/domain/binaryTarget.ts`,
`elfFixedHeader.ts`, `peMachine.ts`, `dosMz.ts`, `dosCom.ts`, and
`apple/machoContainer.ts` / `apple/jvmClassFile.ts`, now only in Git history.
This is a semantic port with C# regression cases, not a claim that the entire
original test suite or every parser edge case has been verified for parity.

The SHA-256 covers exactly the retained bytes at offset zero. It is **not** a
whole-target digest. The result preserves the caller's path separately from its
absolute lookup path. Missing debug data or ABI records outside the prefix remain
unknown. File sharing prevents ordinary concurrent writes on Windows; it does
not establish an atomic cross-platform filesystem snapshot. Full-file identity,
Evidence envelopes, session retention, and live-state cache validation remain
unported; neither result is an Evidence substitute. The managed command has a
full-snapshot digest; the header command retains its prefix-digest semantics.

No provider binaries or provider engine projects are opened by these tests.
Real Hopper, Ghidra, IDA, Frida, browser, and debugger behavior is unverified in
the C# implementation. Managed tests inspect a real compiled fixture with an
initializer that throws if its module executes, and cover resource hashes,
damaged rows, references, ReadyToRun classification, bitness flags, CLI JSON, and
a real CLI subprocess. This does not establish complete parity with the legacy
managed implementation. General attribute arguments, member signatures, method
bodies, native-boundary declarations, and managed reconstruction remain unported.
The short test-suite elapsed time is not a representative REA task benchmark.

### Measured managed workflow

On Windows on 2026-10-15, SDK 10.0.401/runtime 10.0.12 inspected the installed
`System.Text.Json.dll` (1,890,128 bytes,
SHA-256 `fa99f413be21ed5f484a09093fe5f0beba7de3062cf9cc9f847dbf8108dba55a`).
Both invocations returned complete inventory coverage, 15 references, 2,535
custom attributes, and an observed ReadyToRun signature.

| Launch | Elapsed | CLI CPU | Sampled peak CLI working set |
| --- | --- | --- | --- |
| Fresh process, filesystem cache already exercised | 217.02 ms | 156.25 ms | 40.86 MiB |
| Repeat in another fresh process | 219.51 ms | 187.50 ms | 41.09 MiB |

The command launches no child processes, so its process family consists of the
CLI alone. Working-set high-water counters were sampled every 5 ms; the reported
peak is an estimate and can miss allocations near exit. Timings end at process
exit; measurement-client parsing, CPU, and memory are excluded. Filesystem cold
runs, token usage, and an equivalent TypeScript baseline were not measured. No
relative performance improvement is claimed, and these results do not establish
performance for other REA workflows.

## Unavailable functionality after cutover

The following former functionality has been removed and has no C# replacement:

- Domain models and canonical caller-visible schemas beyond executable headers.
- Evidence storage, provenance contracts, sessions, provider selection, caches,
  and analysis/investigation workflows.
- Provider adapters, engine protocols, supervised process ownership, Windows
  native helpers, and cleanup/cancellation across process families.
- Managed member/signature/IL and native-boundary analysis; JavaScript AST analysis and source maps;
  archives, firmware, Android, captures, SQLite, WASM, EVM, and other artifact
  workflows.
- Existing CLI command compatibility and MCP transport, complete tool catalog,
  schema conversion, lifecycle, and client conformance.
- Setup, installers, agent registration, full release publication, and the
  original acceptance and real-provider verification lanes. C# documentation
  generation and local package checks are described in the restored workflow guide.

Restoring any of these capabilities requires a C# implementation and equivalent
public-workflow verification, within the contribution scope. The restored Python,
Java, and Swift engine bridges are the approved source-language exception; their
C# integrations remain unavailable. Required engine-side JavaScript is excluded.

## Package locally

```sh
dotnet pack dotnet/src/Rea.Cli --configuration Release --no-build --output dotnet/artifacts/packages
dotnet exec dotnet/tests/Rea.Tests/bin/Release/net10.0/Rea.Tests.dll --verify-packages dotnet/artifacts/packages
```

The preview package is named `Rea.DotNet.Preview`; its tool command is `rea`.
It is not published by this workflow. Generated assemblies, symbols, and local
packages are ignored by Git. Package checks reject TypeScript/JavaScript files,
Node bindings, node_modules, and bundled test fixtures, and verify the .NET 10
runtime configuration and project dependencies. CI builds and executes only
the C# implementation and its tests.
