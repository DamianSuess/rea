# Current C# checkout workflows

This reference travels with the skill bundle. Commands require a REA source
checkout and the .NET 10 SDK. Installing instructions alone does not install a
CLI, register MCP tools, install engines or grant mutation authority.

From the checkout root:

```text
dotnet build Rea.slnx --configuration Release
dotnet exec dotnet/tests/Rea.Tests/bin/Release/net10.0/Rea.Tests.dll
dotnet exec scripts/bin/Release/net10.0/Rea.Validation.dll repository .
dotnet exec dotnet/src/Rea.Cli/bin/Release/net10.0/rea.dll --help
dotnet exec dotnet/src/Rea.Cli/bin/Release/net10.0/rea.dll inspect-header PATH
dotnet exec dotnet/src/Rea.Cli/bin/Release/net10.0/rea.dll inspect-managed-artifact PATH
```

Only the two inspection commands are available. They read local bytes without
executing targets. The header digest covers retained prefix bytes; the managed
digest covers the full captured file. Preserve caller inputs, provenance,
limitations and unknowns. Consult the checkout's AGENTS.md and CONTRIBUTING.md
before changing code. Do not infer provider authority from archived documentation.

The application, validation and orchestration use C#. Restored engine-required
Python, Java and Swift under `bridge/` are a source-only exception, with no enabled
C# provider integrations. JavaScript/TypeScript application code and engine agents
are not supported. Historical npm, MCP, provider and model-trial examples in the
archived sections are unavailable. Do not execute them as current instructions.

Installation and configuration changes require a displayed plan and approval.
Never save or close an attached IDA GUI database. Human PR readiness declarations
must be completed by the human owner. Tests do not establish real-engine behavior,
maintainer scope approval, or permission to publish or merge.
