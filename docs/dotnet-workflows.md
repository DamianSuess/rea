# Current C# workflows and restored folders

The application currently exposes `inspect-header` and `inspect-managed-artifact`.
Restoring documentation, bridge sources and CI does not restore the former MCP
server or provider adapters. Historical guides carry an explicit status banner.

## Build and verification

From the repository root:

```text
dotnet build Rea.slnx --configuration Release
dotnet exec dotnet/tests/Rea.Tests/bin/Release/net10.0/Rea.Tests.dll --report dotnet/artifacts/reports/test-results.json
dotnet exec dotnet/tools/Rea.Automation/bin/Release/net10.0/Rea.Automation.dll verify-report dotnet/artifacts/reports/test-results.json
```

The test executable runs real assertions. Its report records each case, result
and elapsed time, and missing, empty or failed reports fail validation. There is
no Node/npm dependency. The checked-in C# solution includes the automation and
experiment tools. Generated files belong under `dotnet/artifacts/` or the ignored
experiment `results/` folder, and are not source.

The restored [C# scripts project](../scripts/README.md) adds complete suite
discovery, architecture, current-documentation/skill references, isolated temporary
cleanup and installed-package workflows. CI invokes it before artifact qualification.
Archive-only references are reported as unavailable, not verified. The existing
language policy is still enforced; restored website/native source is not exempted
from it by these validation changes.

## GitHub workflows

- `ci.yml` and `ci-full-pr.yml` invoke reusable `dotnet.yml` verification, including
  package checks and source/digest qualification. Build/test output is retained
  per OS. Windows, Linux and macOS jobs remain independent.
- `pr-title.yml` validates Conventional Commit titles in C# without shell
  interpolation of untrusted titles.
- `pr-readiness.yml` runs only trusted default-branch C# code. It parses the trusted
  template, ignores comments/code blocks, requires one human owner and three exact
  checked declarations, protects against shared PR heads, rereads metadata to detect
  races, and publishes `PR readiness` through the GitHub REST API. Tests use fake
  HTTP responses; no live statuses were posted during this restoration.
- `ci-timings.yml` retains executed test-case timing/failure reports.
- `pages.yml` builds documentation with C#. `website-check.yml` reuses that build.
  `website-pages.yml` is an explicitly dispatched GitHub Pages publication of the
  C#-generated documentation, not the removed custom website.
- `release.yml` prepares source-bound .NET packages and qualification receipts.
  Publishing, recovery and MCP registration fail closed: the reviewed release-note,
  distribution and MCP contracts have not been fully ported. Qualification is not
  publication authority. It does not publish npm packages or NuGet packages.

Qualification requires that the selected SHA equal Git HEAD and that the checkout
have no tracked or untracked source changes. A dirty local tree can still build,
run tests and pass package-structure checks, but cannot emit a misleading
commit-qualified release receipt. Qualified package hashes are not a substitute
for reviewed build provenance or human release authority.

All original workflow filenames remain. Twenty historical provider/mutation/
recovery lanes are manual availability gates that exit unsuccessfully with
`state: unavailable`. They do **not** substitute a passing C# unit test for a real
engine verification. See `.github/workflow-compatibility.json` for the inventory.
Required branch checks may need maintainer configuration after these job changes;
no repository settings were changed remotely.

The original composite-action directories remain. Their implementations now use
.NET setup/restore, C# qualification, and explicit MCP publication availability.
The historical `npm-cache` directory name does not install npm or cache Node data.
The former registry-state module is now `RegistryState.cs` in Rea.Automation,
including exact-version identity, active state and cursor checks.

## Documentation

```text
dotnet exec dotnet/tools/Rea.Automation/bin/Release/net10.0/Rea.Automation.dll docs . dotnet/artifacts/docs-site
```

`docs/site.json` replaces the VitePress TypeScript configuration. This C# builder
produces a static, JavaScript-free HTML site from the preserved Markdown and
assets. It escapes raw HTML and unsafe link schemes. It renders headings, text,
code blocks, links and images; it is not a complete VitePress/CommonMark renderer.
Mermaid, tables and VitePress components may display as source/text. Historical
links into removed implementation files are retained as historical references.

## Bridge exception

The user approved preserving engine-required Python, Java and Swift sources in
`bridge/`. Their 32 source files are restored from
`ef250b14442ad99458ef7ba1e59b78c809238675` and recorded in
`bridge/source-manifest.json`. The core/orchestration language policy permits
only these explicit source exceptions; application packages still exclude them.
They require bring-your-own engines and original dependencies. No real-engine
compilation or execution was performed, and no C# provider is enabled by merely
restoring those sources. See [bridge status](../bridge/README.md).

## Experiments

```text
dotnet run --project experiments/analysis-efficiency --configuration Release --no-build -- measure inspect-managed-artifact dotnet/tests/fixtures/Rea.ManagedFixture/bin/Release/net10.0/Rea.ManagedFixture.dll experiments/analysis-efficiency/results/managed.json 2
dotnet run --project experiments/analysis-efficiency --configuration Release --no-build -- compare experiments/analysis-efficiency/results/managed.json experiments/analysis-efficiency/results/managed.json
```

Create a new report filename for each trial; the runner refuses to overwrite
existing evidence. It records completed public CLI tasks, elapsed time, CLI CPU,
sampled working-set peak, reply byte counts and observed input identity. It never
persists an ambient environment snapshot. CLI processes start fresh; filesystem
cache state is uncontrolled, and measurement-client costs and model tokens remain
unmeasured. Bytes are not tokens. Comparisons reject different commands, paths,
observed input identities and incomplete runs.

Original experiment reports, corpus identities, questions and provenance records
remain. Old Node/Python entry points map to the C# runner in
`experiments/analysis-efficiency/entrypoint-migration.json`. Provider-, MCP-, and
real-model-specific oracles/trials are historical and unavailable in the current
C# runtime; the consolidated runner does not claim full parity with those scripts.
