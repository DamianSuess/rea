# Repository Guidelines

## Implementation and development

The application, repository automation and experiments use C# targeting .NET 10.
Do not add TypeScript, JavaScript, shell-script implementations or engine-side
JavaScript. The user explicitly authorized an exception for restored engine-required
Python, Java and Swift sources in `bridge/`; preserve that exception without treating
it as permission to add other-language application/orchestration code. Bridge sources
are not enabled C# providers. Configuration, documentation and source assets may use
JSON, XML, YAML, Markdown, Mermaid, CSV, SVG, PNG and plain text. Do not restore npm
manifests or workflows that require the removed Node runtime.

Use the SDK selected by `global.json`. From the repository root:

- `dotnet build Rea.slnx --configuration Release`
- `dotnet run --project dotnet/tests/Rea.Tests --configuration Release --no-build`
- `dotnet pack dotnet/src/Rea.Cli --configuration Release --no-build --output dotnet/artifacts/packages`
- `dotnet exec dotnet/tests/Rea.Tests/bin/Release/net10.0/Rea.Tests.dll --verify-packages dotnet/artifacts/packages`

The tests are a dependency-free executable. `dotnet test` does not execute these
assertions. Add new tests to the runner's explicit case inventory. Repository
policy tests reject non-C# source and obsolete build manifests.

## Product and authority

Current supported operations are documented in README.md. Do not infer support
from historical changelogs or removed interfaces. Distinguish observations,
derivations, inferences, and unknowns. Never claim a complete feature-equivalent
port, real-provider verification, or a test result without evidence.

Follow the contribution scope and fail-closed integration rule in CONTRIBUTING.md.
New providers or capabilities involving target execution, injection, mutation,
or protection recovery need an explicit maintainer scope decision before
substantial implementation intended for integration. Exploratory prototypes do
not authorize integration. Do not save or close an attached IDA GUI database.

Preserve caller-selected values, paths, digests, metadata, and local evidence.
Redact transport credentials and explicitly marked sensitive values; do not infer
secrecy from names. Do not persist an inherited ambient environment. Configuration
changes must be additive and idempotent with backups, and setup must show planned
changes and require approval before writing or installing anything.

## Architecture and ownership

Domain semantics and Result contracts are inward dependencies. Application
workflows own target snapshots and file identity. Provider adapters own SDK and
format interpretation; CLI adapters perform input/output translation. See
docs/architecture.mermaid. Keep providers and protocols out of domain/application
code. Bind operations to named, exactly typed contracts.

Give each interpretation and resource one owner. Normalize once at that boundary
and share its parsed facts with consumers. Preserve omission and explicit unknowns;
unexamined data is not absence. Keep proof of ownership separate from validity,
editability, and readiness. Retain ownership after failed cleanup. A PID and path
alone cannot establish process ownership after exit or reuse.

Use tagged Result variants for expected failures. Check lengths, allocation and
representation expansion before expensive work. Preserve admitted partial facts
and meaningful failure reasons. When fixing bugs or review findings, inspect the
producing boundary and consumers, complete related structural cleanup, remove
superseded representations and redundant tests, and explain remaining limitations.

## Verification and performance

Prefer public CLI workflows, then production-boundary integration, then distinct
module semantics. Preserve malformed input, cancellation, capacity, permissions,
cleanup and target identity coverage. Real-provider claims need real-provider
evidence. Validate advertised schemas against their dialect and actual clients
if an MCP adapter is introduced. See docs/testing.md and docs/tool-design.md.

Optimize agent context first, analysis time second, CPU and memory third. Preserve
correctness, provenance, caller choices and bounded resources. Measure equivalent
completed workflows where available; report token usage, elapsed/CPU time and
peak process-family memory, label cold/warm runs and estimates, and disclose
unmeasured costs. Fewer bytes alone do not establish fewer tokens or faster work.

## Changes and review

Use Conventional Commit subjects and PR titles, marking breaking changes with `!`
or a BREAKING CHANGE footer. Do not commit assemblies, packages, provider projects,
credentials, build outputs or local planning artifacts. Keep PR bodies concise
and current with behavior, checks that actually ran and verification gaps.
Address actionable automated findings and explain disagreements.

A human must review and own the final diff and declare it ready for initial
maintainer review. Leave Author readiness declarations unchecked for the human;
keep a PR in draft until that declaration. Do not attest to human review or
interpret tests as maintainer scope approval or merge permission.
