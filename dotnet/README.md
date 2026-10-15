# REA on C# and .NET 10

This is the first working part of the C# migration. It builds, runs, tests, and
packages with the .NET 10 SDK alone. There are no Node, npm, TypeScript, or NuGet
package dependencies in this solution.

**The complete project has not been converted.** The existing implementation is
still required for the full REA CLI and MCP server. This preview exposes only
executable-header inspection. It does not delegate any operation to that
implementation, and it does not advertise unported tools as available.

## Build and run

Run from this directory:

```sh
dotnet build Rea.slnx --configuration Release
dotnet run --project src/Rea.Cli --configuration Release --no-build -- --help
dotnet run --project src/Rea.Cli --configuration Release --no-build -- inspect-header /path/to/binary
dotnet run --project tests/Rea.Tests --configuration Release --no-build
```

The test project is a dependency-free executable, not a VSTest project. Execute
the last command to run its assertions; `dotnet test` is not the verification
command. The runner exits unsuccessfully on any failing assertion or if no tests
are discovered. It includes a real CLI subprocess inspecting the built managed
assembly and deterministic malformed-input and truncation checks.

For direct invocation after a build:

```sh
dotnet exec src/Rea.Cli/bin/Release/net10.0/rea.dll inspect-header /path/to/binary
dotnet exec tests/Rea.Tests/bin/Release/net10.0/Rea.Tests.dll
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
| CLI | `inspect-header`, help and preview version |

The parser source counterparts are `src/domain/binaryTarget.ts`,
`elfFixedHeader.ts`, `peMachine.ts`, `dosMz.ts`, `dosCom.ts`, and
`apple/machoContainer.ts` / `apple/jvmClassFile.ts` in the repository root.
This is a semantic port with C# regression cases, not a claim that the entire
original test suite or every parser edge case has been verified for parity.

The SHA-256 covers exactly the retained bytes at offset zero. It is **not** a
whole-target digest. The result preserves the caller's path separately from its
absolute lookup path. Missing debug data or ABI records outside the prefix remain
unknown. File sharing prevents ordinary concurrent writes on Windows; it does
not establish an atomic cross-platform filesystem snapshot. Full-file identity,
Evidence envelopes, session retention, and live-state cache validation remain
unported; the header result is not an Evidence substitute.

No provider binaries or provider engine projects are opened by these tests.
Real Hopper, Ghidra, IDA, Frida, browser, and debugger behavior is unverified in
the C# implementation. The short test-suite elapsed time is not a representative
REA task benchmark. Token usage, comparative task latency, CPU time, and peak
process-family memory have not been measured.

## Remaining migration

The C# solution does not yet replace these parts of REA:

- Domain models and canonical caller-visible schemas beyond executable headers.
- Evidence storage, provenance contracts, sessions, provider selection, caches,
  and analysis/investigation workflows.
- Provider adapters, engine protocols, supervised process ownership, Windows
  native helpers, and cleanup/cancellation across process families.
- Managed metadata/member/IL analysis; JavaScript AST analysis and source maps;
  archives, firmware, Android, captures, SQLite, WASM, EVM, and other artifact
  workflows.
- Existing CLI command compatibility and MCP transport, complete tool catalog,
  schema conversion, lifecycle, and client conformance.
- Setup, installers, agent registration, documentation generation, release
  packaging, and the original acceptance and real-provider verification lanes.

These need implementations and equivalent public-workflow verification before
the TypeScript runtime, tests, scripts, package manifests, and build configuration
can be retired. Existing Python, Java, Swift, C/C++, and target-side JavaScript
bridges also need individual decisions about keeping a required host-language
bridge or replacing it; changing the orchestration language alone does not
eliminate those engine constraints.

## Package locally

```sh
dotnet pack src/Rea.Cli --configuration Release --no-build --output artifacts/packages
```

The preview package is named `Rea.DotNet.Preview`; its tool command is `rea`.
It is not published by this workflow. Generated assemblies, symbols, and local
packages are ignored by Git. CI builds and executes this C# test suite separately
from the existing implementation.
