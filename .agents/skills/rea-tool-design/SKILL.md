---
name: rea-tool-design
description: Design or change REA investigation tools, CLI/MCP contracts, provider capabilities, and Evidence semantics.
---

> **C# compatibility status:** Restored from `ef250b14442ad99458ef7ba1e59b78c809238675`. The text below records the former implementation. Follow [the current C# guide](references/current-csharp.md) for executable commands. Historical npm, MCP, provider and model-trial examples are not supported by the current C# runtime. Do not treat historical verification as current verification.

## Active C# checkout instructions

Use the repository root `global.json` and `Rea.slnx`. Build with
`dotnet build Rea.slnx --configuration Release`; execute the test harness with
`dotnet exec dotnet/tests/Rea.Tests/bin/Release/net10.0/Rea.Tests.dll`.
See the current C# guide linked above for Rea.Automation and Rea.Experiments.
Only inspect-header and inspect-managed-artifact are available analysis commands.
Preserve the principles below, but do not execute historical npm, MCP, generated
catalog, provider or release commands. Restored engine bridge source is not an
enabled provider and does not establish real-engine verification or authority.

## Historical implementation guidance



# REA tool design

Use the [tool-design guide](../../../docs/tool-design.md) for tool boundaries,
contract semantics, provider ownership, and discoverability. Inspect the current
contract and nearest existing tool relevant to the analyst question; source,
documentation, and live catalogs may describe different versions.

For a design request, return the proposed boundary, input/result shape, Evidence
semantics, nearest alternative, and verification approach. Implement when
requested, preserving the user's scope and existing authorization.

For implementation or verification, consult [testing](../../../docs/testing.md)
for the affected consumer and provider lanes, and
[contributing](../../../CONTRIBUTING.md) for generated-file ownership. Read
provider/workflow guides only as the task requires.
