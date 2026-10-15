# C# repository validation

The former `scripts/` JavaScript/Python checks are replaced by this .NET 10 C#
project. No Node.js or JavaScript is needed. Build the root solution first.
The [migration inventory](validation-migration.json) records original entrypoints,
their replacements and remaining unavailable coverage.

```text
dotnet build Rea.slnx --configuration Release
dotnet exec scripts/bin/Release/net10.0/Rea.Validation.dll repository .
dotnet exec scripts/bin/Release/net10.0/Rea.Validation.dll test-hygiene . dotnet/artifacts/reports/test-results.json
dotnet pack dotnet/src/Rea.Cli --configuration Release --no-build --output dotnet/artifacts/packages
dotnet exec scripts/bin/Release/net10.0/Rea.Validation.dll package . dotnet/artifacts/packages
```

- Test discovery automatically enumerates compiled test suites and compares them
  with the source inventory. Missing, unexpected, duplicate and empty suites fail;
  duplicate case names fail. Package tests are a separate explicit selection.
- Experiment sanity cases use real C# CLI workflows and controlled C# children to
  check measurement identities, report comparisons, output limits, deadlines,
  cancellation and exclusive-file release. See the [experiment guide](../experiments/analysis-efficiency/CSharp.md).
- Architecture validation checks project references and compiled assembly
  references against the allowed domain, application, managed-provider and CLI
  dependency directions. New application projects require a policy entry.
- Documentation validation checks current local links and heading fragments, required
  supported-command documentation, and active skill metadata and bundle references.
  The Markdown subset covers inline/reference links, ATX headings, fences, inline
  code and HTML comments. External links are not network-probed. Historical guides
  remain marked as historical; unresolved archived references are reported separately
  as `historical-unavailable`, never as verified support. Full CommonMark and old
  provider/model contracts are outside current coverage.
- Test hygiene runs the real test executable in a fresh isolated TEMP/TMP/TMPDIR,
  fails on any remaining filesystem entry, and preserves failures for investigation.
  The runner's report must contain executed passing cases. This does not establish
  absence of unrelated OS resources or descendants after arbitrary detached processes.
- Package validation installs the actual `.nupkg` using a private cache and local
  feed, runs both analysis commands from outside the checkout, checks managed
  identity/digest, missing-input and syntax failures, and input preservation. The
  fixture's module initializer throws if executed. Reports and installed-tool
  workspaces are deliberately retained under ignored `dotnet/artifacts/validation/`.

Current CI runs these gates on Windows, Linux and macOS. A workflow definition is
not evidence of an executed remote run. Engine-specific legacy verifiers remain
recoverable in Git and are not advertised as ported or verified providers.

Reports contain local paths and analysis facts; ambient environment values are
not serialized. The commands do not publish packages, contact provider engines,
or modify user configuration.

The existing C# source-policy test also reports restored website/native sources
outside the approved bridge exception. Those files were explicitly left untouched
in this change. The complete gate remains unsuccessful while that policy fails;
the validators do not suppress it. Historical link reports and unavailable providers
are separate from this actionable current policy failure.
