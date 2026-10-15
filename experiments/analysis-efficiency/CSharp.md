# C# experiment entry point

Build the root .NET solution, then use Rea.Experiments `measure` and `compare`.
See [the current guide](../../docs/dotnet-workflows.md#experiments) for exact commands,
metrics, output ownership and limitations.

The restored reports, corpus JSON and question files retain their original
evidence and attribution. They are not current verification. The 33 former
Node/Python entry points are mapped in `entrypoint-migration.json` with the
original source hashes. The new runner measures the two supported C# commands;
provider/MCP/model-specific behavior remains unavailable rather than simulated.

## Sanity checks

The normal C# test executable discovers `ExperimentTests` automatically. It runs
both real inspection commands against the compiled fixture, exercises `measure`,
`verify` and `compare`, and injects malformed reports, changed inputs, forged
digests, incomplete tasks, differing coverage and measurement settings. Existing
reports must remain byte-for-byte unchanged on a refused overwrite. Failed CLI
measurements retain a `status: failed` report that cannot compare successfully.

```text
dotnet build Rea.slnx --configuration Release
dotnet exec scripts/bin/Release/net10.0/Rea.Validation.dll test-hygiene . dotnet/artifacts/reports/test-results.json
dotnet run --project experiments/analysis-efficiency --configuration Release --no-build -- verify NEW_REPORT_JSON
```

The process checks use controlled C# children, covering nonzero exits, separate
stdout/stderr budgets, a deadline, and cancellation that releases an exclusive
file lease. They execute no target or provider code. Cleanup requests termination
of the owned process tree and verifies the direct process exit; that is not proof
that arbitrary detached descendants were terminated. Failed cleanup retains the
process owner in its exception. Native Job Objects/systemd scopes, CPU/memory
enforcement, whole-process-family counters and real-engine cleanup remain unported.

## Report contract

New measurements use schema 2. Schema 1 reports remain historical evidence and
must be regenerated for the strengthened comparison checks. The runner independently
hashes input bytes before and after each task, checking the CLI's reported caller
input, digest and byte extent. Header hashes cover only the default retained prefix
(up to 65,536 bytes); managed hashes cover the full admitted file. Neither establishes
an atomic filesystem snapshot. CLI assembly hashes must remain stable during a run.

`verify` additionally checks the report against the current local input and CLI
bytes. `compare` checks recorded schema, successful runs, finite counters, identical
input identity/coverage, caller input, runtime/platform and measurement settings.
It does not authenticate report authorship or prove identical host load/cache state.
Unknown CPU/memory counters and model tokens remain explicit null, never zero.
The 16 MiB report budget, 30-second child deadline, 64 MiB stdout and 1 MiB stderr
budgets bound normal measurements; cleanup has a separate deadline. Filesystem cache
state, client costs and model tokens remain unmeasured; no speedup is claimed.
