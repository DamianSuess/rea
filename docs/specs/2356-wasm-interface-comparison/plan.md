> **C# compatibility status:** Restored from `ef250b14442ad99458ef7ba1e59b78c809238675`. The text below records the former implementation. Follow [the current C# guide](../../dotnet-workflows.md) for executable commands. Historical npm, MCP, provider and model-trial examples are not supported by the current C# runtime. Do not treat historical verification as current verification.

# Plan

1. Check issue/timeline and complete open/closed PR searches; review #2406.
2. Extend the existing core-module WABT inspection in an isolated worktree.
3. Extend producer-owned inspection with optional typed interface facts;
   preserve unknown unsupported signatures and exact WAT forms.
4. Add shared Evidence-only comparator and strict input/output contracts.
5. Register additive CLI/MCP workflow and documentation.
6. Validate original-module workflows, malformed/unknown/cancellation cases;
   run checks, independent review, repair findings and rerun affected checks.
