# Verification

From the repository root, build with `dotnet build Rea.slnx --configuration Release`
and execute `dotnet run --project dotnet/tests/Rea.Tests --configuration Release --no-build`.
This is a dependency-free executable test harness; `dotnet test` does not run its
assertions. Zero discovered cases or a failed assertion exits unsuccessfully.

Test suites are discovered automatically and compared with the `*Tests.cs` source
inventory. Missing/extra/duplicate suites, empty suites and duplicate case names
fail before execution. Use the [C# scripts project](../scripts/README.md) for the
repository, isolated cleanup and installed-package gates used by CI. It replaces
the former JavaScript/Python validation entrypoints.

Prefer real public CLI workflows, then provider-boundary integration, then module
tests for distinct semantics. Preserve malformed-input, cancellation, permission,
capacity, cleanup and identity coverage. The managed fixture is built from C#;
its module initializer throws if target code executes. Tests must inspect its
bytes without loading it. No external engine/provider support is established.

Repository policy tests scan source/configuration and reject foreign source
extensions and legacy build manifests, except for the explicitly approved and
receipt-checked engine bridge sources. Restored documentation images/CSV are data.
Compiler outputs and local generated
packages are excluded from source-language checks. Package tests inspect the
actual archive and verify dependencies and absence of Node/JavaScript/TypeScript.

Package with the commands in README.md, then run the test executable with
`--verify-packages dotnet/artifacts/packages`. CI uses these same commands on
Windows, Linux, and macOS. Report only checks executed, distinguish local and CI
results, and do not infer whole-feature parity from regression tests.

Use `--report dotnet/artifacts/reports/test-results.json` to retain case results and
timings. Validate the report with `Rea.Automation verify-report`; missing/empty or
failed reports fail the gate. See [restored C# automation](dotnet-workflows.md).
